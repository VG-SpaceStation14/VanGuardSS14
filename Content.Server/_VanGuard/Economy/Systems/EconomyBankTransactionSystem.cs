using System.Linq;
using System.Collections.Generic;
using Content.Server._VanGuard.Economy.Components;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared._VanGuard.Economy;
using Content.Shared.Ghost.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Stacks;
using Content.Shared.Station.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Economy.Systems;

/// <summary>
///     Server-authoritative deposit/withdraw and transfer operations shared by
///     the PDA bank cartridge and ATMs. Request ids make retries idempotent.
/// </summary>
public sealed partial class EconomyBankTransactionSystem : EntitySystem
{
    private const int MaxTransaction = BankTransactionLimits.MaxTransaction;
    private const int MaxCachedRequests = 4096;

    [Dependency] private EconomyBankSystem _bank = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private ServerStationSystem _station = default!;

    private static readonly ProtoId<StackPrototype> CreditStack = "Credit";

    private readonly Dictionary<(string AccountId, int RequestId), BankRequestOutcome> _processed = new();
    private readonly Queue<(string AccountId, int RequestId)> _requestOrder = new();
    private long _nextTransferId = 1;

    public BankTransactionStatus Process(
        EntityUid user,
        BankTransactionRequest request,
        out BankAccountUiState? state)
    {
        state = null;

        if (request.Operation != BankOperation.DepositAll
            && (request.Amount <= 0 || request.Amount > MaxTransaction))
        {
            return BankTransactionStatus.InvalidAmount;
        }

        if (!_bank.TryGetPlayerAccount(user, out var mindUid, out var account))
            return BankTransactionStatus.NoAccount;

        var key = (account.AccountId, request.RequestId);
        if (_processed.TryGetValue(key, out var cached))
        {
            // A retry must never perform the operation a second time. Rebuild
            // the current state so the UI can show that the request was already
            // handled without revealing the previous balance snapshot. The
            // effective amount is cached so "deposit all" still reports what
            // was actually deposited.
            var displayRequest = cached.Amount is { } cachedAmount
                ? request with { Amount = cachedAmount }
                : request;

            state = BuildState((mindUid, account), BankTransactionStatus.DuplicateRequest,
                displayRequest, cash: GetHeldCash(user));
            if (cached.TransactionStatus is not { } cachedStatus)
                return BankTransactionStatus.InvalidRequest;

            return cachedStatus == BankTransactionStatus.Success
                ? BankTransactionStatus.DuplicateRequest
                : cachedStatus;
        }

        var effectiveRequest = request;
        if (request.Operation == BankOperation.DepositAll)
        {
            var cash = GetHeldCash(user);
            if (cash <= 0)
            {
                state = BuildState((mindUid, account), BankTransactionStatus.NoCash,
                    request with { Amount = 0 }, cash: 0);
                return BankTransactionStatus.NoCash;
            }

            // The operation limit is per request. Deposit what fits and leave
            // the rest in the player's hands rather than rejecting everything.
            effectiveRequest = request with { Amount = Math.Min(cash, MaxTransaction) };
        }

        var status = effectiveRequest.Operation switch
        {
            BankOperation.Deposit or BankOperation.DepositAll =>
                Deposit(user, (mindUid, account), effectiveRequest.Amount),
            BankOperation.Withdraw => Withdraw(user, (mindUid, account), effectiveRequest.Amount),
            _ => BankTransactionStatus.InvalidRequest,
        };

        Remember(key, new BankRequestOutcome(status, null, effectiveRequest.Amount));
        state = BuildState((mindUid, account), status, effectiveRequest, cash: GetHeldCash(user));
        return status;
    }

    public BankAccountUiState BuildState(
        Entity<EconomyAccountComponent> account,
        BankTransactionStatus status = BankTransactionStatus.InvalidRequest,
        BankTransactionRequest? request = null,
        bool hasCard = true,
        bool authorized = true,
        string cardOwnerName = "",
        int cash = 0)
    {
        var transactions = account.Comp.History
            .TakeLast(20)
            .Reverse()
            .Select(transaction => new BankUiTransaction(
                transaction.Time,
                transaction.Delta,
                transaction.Reason,
                transaction.ReasonData,
                transaction.CounterpartyName,
                transaction.CounterpartyAccount,
                transaction.TransferId))
            .ToList();

        return new BankAccountUiState(
            account.Comp.AccountId,
            account.Comp.OwnerName,
            account.Comp.Balance,
            transactions,
            status,
            request?.RequestId ?? 0,
            request?.Operation ?? BankOperation.Deposit,
            request?.Amount ?? 0,
            hasCard,
            authorized,
            cardOwnerName,
            cash);
    }

    /// <summary>
    ///     Executes a player-to-player transfer after resolving and re-validating
    ///     both accounts. The request key is shared with ATM operations so the
    ///     same account/request id can never execute twice.
    /// </summary>
    public BankTransferStatus Transfer(
        EntityUid user,
        NetEntity recipient,
        int amount,
        int requestId,
        out BankTransferResult result)
    {
        result = default;

        if (requestId <= 0)
            return BankTransferStatus.InvalidRequest;

        if (!_bank.TryGetPlayerAccount(user, out var senderMindUid, out var senderAccount)
            || !EconomyBankSystem.IsValidAccountId(senderAccount.AccountId))
        {
            return BankTransferStatus.NoAccount;
        }

        if (!TryComp<MindComponent>(senderMindUid, out var senderMind)
            || senderMind.OwnedEntity is not { } senderBody
            || !Exists(senderBody)
            || !TryComp<MindContainerComponent>(senderBody, out var senderContainer)
            || senderContainer.Mind != senderMindUid)
        {
            return BankTransferStatus.NoAccount;
        }

        var senderStation = GetStation(senderBody);
        if (senderStation == null)
            return BankTransferStatus.NoAccount;

        var key = (senderAccount.AccountId, requestId);
        if (_processed.TryGetValue(key, out var cached))
        {
            if (cached.TransferStatus is not { } cachedStatus)
                return BankTransferStatus.InvalidRequest;

            return cachedStatus == BankTransferStatus.Success
                ? BankTransferStatus.DuplicateRequest
                : cachedStatus;
        }

        if (amount <= 0 || amount > MaxTransaction)
            return RememberTransferFailure(key, BankTransferStatus.InvalidAmount);

        if (!TryGetEntity(recipient, out var recipientMindUid))
            return RememberTransferFailure(key, BankTransferStatus.RecipientNotFound);

        if (recipientMindUid.Value == senderMindUid)
            return RememberTransferFailure(key, BankTransferStatus.SelfTransfer);

        if (!Exists(recipientMindUid.Value)
            || !TryComp<EconomyAccountComponent>(recipientMindUid.Value, out var recipientAccount)
            || !EconomyBankSystem.IsValidAccountId(recipientAccount.AccountId))
        {
            return RememberTransferFailure(key, BankTransferStatus.RecipientNoAccount);
        }

        if (!TryComp<MindComponent>(recipientMindUid.Value, out var recipientMind)
            || (recipientMind.UserId == null && recipientMind.OriginalOwnerUserId == null)
            || recipientMind.OwnedEntity is not { } recipientBody
            || !Exists(recipientBody)
            || !TryComp<MindContainerComponent>(recipientBody, out var recipientContainer)
            || recipientContainer.Mind != recipientMindUid.Value
            || HasComp<GhostComponent>(recipientBody))
        {
            return RememberTransferFailure(key, BankTransferStatus.RecipientNotFound);
        }

        if (string.Equals(recipientAccount.AccountId, senderAccount.AccountId, StringComparison.Ordinal))
            return RememberTransferFailure(key, BankTransferStatus.SelfTransfer);

        if (GetStation(recipientBody) != senderStation)
            return RememberTransferFailure(key, BankTransferStatus.RecipientNotOnStation);

        if (senderAccount.Balance < amount)
            return RememberTransferFailure(key, BankTransferStatus.InsufficientFunds);

        var senderName = GetName(senderMind, senderAccount);
        var recipientName = GetName(recipientMind, recipientAccount);
        var transferId = _nextTransferId;

        if (!_bank.TransferAtomic(
                (senderMindUid, senderAccount),
                (recipientMindUid.Value, recipientAccount),
                amount,
                "bank-transfer-out",
                "bank-transfer-in",
                transferId,
                senderName,
                recipientName))
        {
            return RememberTransferFailure(key, BankTransferStatus.TransferFailed);
        }

        _nextTransferId++;
        Remember(key, new BankRequestOutcome(null, BankTransferStatus.Success));
        result = new BankTransferResult(
            BankTransferStatus.Success,
            amount,
            recipientBody,
            senderName,
            recipientName,
            transferId);
        return BankTransferStatus.Success;
    }

    private EntityUid? GetStation(EntityUid body)
    {
        if (_station.GetOwningStation(body) is { } station)
            return station;

        var xform = Transform(body);
        if (xform.GridUid is not { } grid)
            return null;

        if (TryComp<StationMemberComponent>(grid, out var member))
            return member.Station;

        // Some admin-loaded or remapped grids can have station data without
        // the usual StationMember component. Keep the same station boundary;
        // never fall back to "any grid".
        foreach (var stationEntity in _station.GetStations())
        {
            var stationCandidate = new Entity<StationDataComponent?>(stationEntity.Owner, stationEntity.Comp);
            if (_station.GetLargestGrid(stationCandidate) == grid)
                return stationEntity.Owner;
        }

        return null;
    }

    private BankTransferStatus RememberTransferFailure(
        (string AccountId, int RequestId) key,
        BankTransferStatus status)
    {
        Remember(key, new BankRequestOutcome(null, status));
        return status;
    }

    private static string GetName(MindComponent mind, EconomyAccountComponent account)
    {
        if (!string.IsNullOrWhiteSpace(mind.CharacterName))
            return mind.CharacterName!;

        return account.OwnerName;
    }

    private BankTransactionStatus Deposit(
        EntityUid user,
        Entity<EconomyAccountComponent> account,
        int amount)
    {
        if (!TryConsumeCash(user, amount))
            return BankTransactionStatus.NoCash;

        if (!_bank.Deposit(account, amount, "bank-deposit", GetNetEntity(user)))
        {
            // The cash was consumed but the account credit failed. Return the
            // exact amount as a new stack rather than silently deleting money.
            RefundCash(user, amount);
            return BankTransactionStatus.InvalidRequest;
        }

        return BankTransactionStatus.Success;
    }

    private BankTransactionStatus Withdraw(
        EntityUid user,
        Entity<EconomyAccountComponent> account,
        int amount)
    {
        if (account.Comp.Balance < amount)
            return BankTransactionStatus.InsufficientFunds;

        if (!_bank.Withdraw(account, amount, "bank-withdraw", GetNetEntity(user)))
            return BankTransactionStatus.InsufficientFunds;

        var cash = _stacks.SpawnAtPosition(amount, CreditStack, Transform(user).Coordinates);
        _hands.PickupOrDrop(user, cash);
        return BankTransactionStatus.Success;
    }

    /// <summary>
    ///     Total credits currently held in the player's hands. This is a
    ///     read-only UI value; the operation still re-counts and consumes the
    ///     physical stacks server-side.
    /// </summary>
    public int GetHeldCash(EntityUid user)
    {
        var total = 0;
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (TryComp(held, out StackComponent? stack) && stack.StackTypeId == CreditStack)
                total += stack.Count;
        }

        return total;
    }

    private bool TryConsumeCash(EntityUid user, int amount)
    {
        var stacks = new List<(EntityUid Uid, StackComponent Stack)>();
        var total = 0;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (!TryComp(held, out StackComponent? stack) || stack.StackTypeId != CreditStack)
                continue;

            stacks.Add((held, stack));
            total += stack.Count;
        }

        if (total < amount)
            return false;

        var remaining = amount;
        var consumed = 0;
        foreach (var (uid, stack) in stacks)
        {
            if (remaining <= 0)
                break;

            var take = Math.Min(remaining, stack.Count);
            if (!_stacks.TryUse((uid, stack), take))
            {
                // Do not lose money already taken from earlier stacks if a
                // later stack unexpectedly fails to update.
                RefundCash(user, consumed);
                return false;
            }

            consumed += take;
            remaining -= take;
            if (stack.Count <= 0)
                QueueDel(uid);
        }

        if (remaining != 0)
        {
            RefundCash(user, consumed);
            return false;
        }

        return true;
    }

    private void RefundCash(EntityUid user, int amount)
    {
        if (amount <= 0)
            return;

        var refund = _stacks.SpawnAtPosition(amount, CreditStack, Transform(user).Coordinates);
        _hands.PickupOrDrop(user, refund);
    }

    private void Remember((string AccountId, int RequestId) key, BankRequestOutcome outcome)
    {
        _processed[key] = outcome;
        _requestOrder.Enqueue(key);

        while (_requestOrder.Count > MaxCachedRequests)
        {
            var oldest = _requestOrder.Dequeue();
            _processed.Remove(oldest);
        }
    }

    private readonly record struct BankRequestOutcome(
        BankTransactionStatus? TransactionStatus,
        BankTransferStatus? TransferStatus,
        int? Amount = null);
}

/// <summary>
///     Result of a player-to-player transfer. RecipientBody is only set on
///     success and is used to deliver the incoming-transfer notification.
/// </summary>
public readonly record struct BankTransferResult(
    BankTransferStatus Status,
    int Amount,
    EntityUid? RecipientBody,
    string? SenderName,
    string? RecipientName,
    long TransferId);

[ByRefEvent]
public readonly record struct BankAccountChangedEvent(EntityUid AccountOwner);
