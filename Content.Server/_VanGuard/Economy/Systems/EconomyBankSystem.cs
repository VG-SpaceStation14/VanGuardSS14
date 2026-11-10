using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server._VanGuard.Economy.Components;
using Content.Server.Mind;
using Content.Shared.Database;
using Content.Shared.Mind;
using Content.Shared._VanGuard.Economy;
using Content.Shared._VanGuard.Mind;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Economy.Systems;

/// <summary>
///     Owns all personal bank accounts. Accounts live on the mind entity and are
///     addressed by a unique 12-digit account id. Every balance change is recorded
///     in the account history and mirrored into the admin log.
/// </summary>
public sealed partial class EconomyBankSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly ISawmill _sawmill = Logger.GetSawmill("economy-bank");

    public override void Initialize()
    {
        SubscribeLocalEvent<EconomyAccountComponent, ComponentStartup>(OnAccountStartup);
    }

    private void OnAccountStartup(Entity<EconomyAccountComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<MindComponent>(ent, out var mind) && !string.IsNullOrWhiteSpace(mind.CharacterName))
            ent.Comp.OwnerName = mind.CharacterName;
    }

    /// <summary>
    ///     Ensures the mind has a valid personal account, generating a fresh id and
    ///     syncing the owner name when necessary.
    /// </summary>
    public EconomyAccountComponent EnsureAccount(EntityUid mindUid, MindComponent? mind = null)
    {
        var account = EnsureComp<EconomyAccountComponent>(mindUid);

        if (!IsValidAccountId(account.AccountId))
            account.AccountId = GenerateUniqueAccountId();

        if (string.IsNullOrWhiteSpace(account.Pin) || !account.Pin.All(char.IsDigit) || account.Pin.Length != 4)
            account.Pin = GeneratePin();

        // Accounts created by the previous implementation may already have
        // received a starting payment. Do not mint that payment again.
        if (account.StartingPayrollReceived)
            account.StartingBalancePaid = true;

        if (Resolve(mindUid, ref mind, false) && !string.IsNullOrWhiteSpace(mind.CharacterName)
            && account.OwnerName != mind.CharacterName)
            account.OwnerName = mind.CharacterName;

        if (Resolve(mindUid, ref mind, false))
        {
            mind.AddMemory(new Memory("character-info-memory-pin", account.Pin));
            mind.AddMemory(new Memory("character-info-memory-account", account.AccountId));
        }

        return account;
    }

    public static bool IsValidAccountId(string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId) || accountId.Length != 12)
            return false;

        return accountId.All(char.IsDigit);
    }

    private string GeneratePin()
    {
        return _random.Next(1000, 10000).ToString();
    }

    private string GenerateUniqueAccountId()
    {
        Span<char> digits = stackalloc char[12];

        while (true)
        {
            digits[0] = (char)('1' + _random.Next(9));
            for (var i = 1; i < digits.Length; i++)
                digits[i] = (char)('0' + _random.Next(10));

            var candidate = new string(digits);
            if (!TryFindAccountById(candidate, out _))
                return candidate;
        }
    }

    /// <summary>
    ///     Returns the account of the mind owning <paramref name="playerEntity"/>.
    /// </summary>
    public bool TryGetPlayerAccount(EntityUid playerEntity, out EntityUid mindUid, out EconomyAccountComponent account)
    {
        account = default!;
        mindUid = default;

        if (!_mind.TryGetMind(playerEntity, out mindUid, out _))
            return false;

        if (!TryComp(mindUid, out EconomyAccountComponent? found))
            return false;

        account = found;
        return true;
    }

    public bool TryFindAccountById(string accountId, out Entity<EconomyAccountComponent> account)
    {
        var query = EntityQueryEnumerator<EconomyAccountComponent>();
        while (query.MoveNext(out var uid, out var acc))
        {
            if (!string.Equals(acc.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
                continue;

            account = (uid, acc);
            return true;
        }

        account = default;
        return false;
    }

    public static int GetBalance(Entity<EconomyAccountComponent> account)
    {
        return account.Comp.Balance;
    }

    public bool Deposit(Entity<EconomyAccountComponent> account, int amount, string reason, NetEntity? counterparty = null, string? reasonData = null)
    {
        if (amount <= 0)
            return false;

        return AdjustBalance(account, amount, reason, counterparty, reasonData);
    }

    public bool Withdraw(Entity<EconomyAccountComponent> account, int amount, string reason, NetEntity? counterparty = null, string? reasonData = null)
    {
        if (amount <= 0 || account.Comp.Balance < amount)
            return false;

        return AdjustBalance(account, -amount, reason, counterparty, reasonData);
    }

    public bool Transfer(Entity<EconomyAccountComponent> from, Entity<EconomyAccountComponent> to, int amount, string reason)
    {
        return TransferAtomic(from, to, amount, reason, reason);
    }

    /// <summary>
    ///     Moves money between two accounts as one operation. Both resulting
    ///     balances are computed before either balance or history is touched, so
    ///     a failure cannot leave a half-completed transfer behind.
    /// </summary>
    public bool TransferAtomic(
        Entity<EconomyAccountComponent> from,
        Entity<EconomyAccountComponent> to,
        int amount,
        string reasonOut,
        string reasonIn,
        long transferId = 0,
        string? fromName = null,
        string? toName = null)
    {
        if (amount <= 0
            || from.Owner == to.Owner
            || string.Equals(from.Comp.AccountId, to.Comp.AccountId, StringComparison.Ordinal)
            || from.Comp.Balance < amount)
        {
            return false;
        }

        int newFromBalance;
        int newToBalance;
        try
        {
            checked
            {
                newFromBalance = from.Comp.Balance - amount;
                newToBalance = to.Comp.Balance + amount;
            }
        }
        catch (OverflowException)
        {
            _sawmill.Warning($"Transfer overflow from {from.Comp.AccountId} to {to.Comp.AccountId}. Amount: {amount}.");
            return false;
        }

        from.Comp.Balance = newFromBalance;
        to.Comp.Balance = newToBalance;

        fromName = string.IsNullOrWhiteSpace(fromName) ? GetAccountOwnerName(from) : fromName;
        toName = string.IsNullOrWhiteSpace(toName) ? GetAccountOwnerName(to) : toName;
        var fromAccount = BankAccountFormat.Mask(from.Comp.AccountId);
        var toAccount = BankAccountFormat.Mask(to.Comp.AccountId);
        var linkedTransferId = transferId == 0 ? (long?) null : transferId;

        AddTransaction(from, -amount, reasonOut, null, GetNetEntity(to.Owner), toName, toAccount, linkedTransferId);
        AddTransaction(to, amount, reasonIn, null, GetNetEntity(from.Owner), fromName, fromAccount, linkedTransferId);

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Account {from.Comp.AccountId} sent {amount} to {to.Comp.AccountId}. Transfer id: {transferId}. New balance: {from.Comp.Balance}");
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Account {to.Comp.AccountId} received {amount} from {from.Comp.AccountId}. Transfer id: {transferId}. New balance: {to.Comp.Balance}");

        var fromChanged = new BankAccountChangedEvent(from.Owner);
        RaiseLocalEvent(ref fromChanged);
        var toChanged = new BankAccountChangedEvent(to.Owner);
        RaiseLocalEvent(ref toChanged);
        return true;
    }

    private string GetAccountOwnerName(Entity<EconomyAccountComponent> account)
    {
        if (TryComp<MindComponent>(account.Owner, out var mind) && !string.IsNullOrWhiteSpace(mind.CharacterName))
            return mind.CharacterName!;

        return account.Comp.OwnerName;
    }

    private bool AdjustBalance(Entity<EconomyAccountComponent> account, int delta, string reason, NetEntity? counterparty = null, string? reasonData = null)
    {
        if (delta == 0)
            return true;

        try
        {
            checked
            {
                account.Comp.Balance += delta;
            }

            AddTransaction(account, delta, reason, reasonData, counterparty);

            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"Account {account.Comp.AccountId} ({account.Comp.OwnerName}) adjusted by {delta}. Reason: {reason}. New balance: {account.Comp.Balance}");

            // Notify every open account view (PDA, ATM, multiple PDAs) and
            // every payroll/ATM operation through the same event.
            var changed = new BankAccountChangedEvent(account.Owner);
            RaiseLocalEvent(ref changed);
            return true;
        }
        catch (OverflowException)
        {
            _sawmill.Error($"Failed to adjust account {account.Comp.AccountId} by {delta}: integer overflow.");
            return false;
        }
    }

    private void AddTransaction(
        Entity<EconomyAccountComponent> account,
        int delta,
        string reason,
        string? reasonData,
        NetEntity? counterparty,
        string? counterpartyName = null,
        string? counterpartyAccount = null,
        long? transferId = null)
    {
        account.Comp.History.Add(new EconomyTransaction(
            _timing.CurTime,
            delta,
            account.Comp.Balance,
            reason,
            reasonData,
            counterparty,
            counterpartyName,
            counterpartyAccount,
            transferId));
        if (account.Comp.History.Count <= account.Comp.MaxHistory)
            return;

        account.Comp.History.RemoveRange(0, account.Comp.History.Count - account.Comp.MaxHistory);
    }
}

