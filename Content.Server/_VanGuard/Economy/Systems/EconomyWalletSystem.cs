using System.Linq;
using Content.Server.Access.Systems;
using Content.Server._VanGuard.Economy.Components;
using Content.Server.Mind;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared._VanGuard.Economy;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Economy.Systems;

/// <summary>
///     Turns every crew ID card into a bank card: shows the linked account balance,
///     allows withdrawing physical credits and depositing cash stacks back onto the
///     account. Accounts are created lazily when a humanoid mind joins the round.
/// </summary>
public sealed partial class EconomyWalletSystem : EntitySystem
{
    [Dependency] private EconomyBankSystem _bank = default!;
    [Dependency] private IdCardSystem _idCard = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly ProtoId<StackPrototype> CreditStackId = "Credit";

    private float _uiRefreshAccumulator;
    private readonly Dictionary<EntityUid, string> _openUiAccounts = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<MindContainerComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<RoleAddedEvent>(OnRoleAdded);
        // VG-Tweak: keep OwningStation in sync without polling every mind every
        // second - an owned entity changing parent (grid/station move) is the
        // only way a station can change after spawn.
        SubscribeLocalEvent<EntParentChangedMessage>(OnOwnedEntityParentChanged);
        SubscribeLocalEvent<IdCardComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<IdCardComponent, BoundUIClosedEvent>(OnUiClosed);
        // VG-Tweak: the StickerSystem already owns the IdCardComponent/InteractUsingEvent
        // slot, so cash deposits are handled through AfterInteractEvent instead:
        // - cash stack clicked onto the card (Orion behaviour)
        // - card clicked onto a cash stack
        SubscribeLocalEvent<StackComponent, AfterInteractEvent>(OnCashUsedOnCard);
        SubscribeLocalEvent<IdCardComponent, AfterInteractEvent>(OnCardUsedOnCash);

        Subs.BuiEvents<IdCardComponent>(EconomyAccountUiKey.Key,
            subs =>
            {
                subs.Event<EconomyWithdrawMessage>(OnWithdrawMessage);
                subs.Event<EconomySelectAccountMessage>(OnSelectAccountMessage);
            });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _uiRefreshAccumulator += frameTime;
        if (_uiRefreshAccumulator < 1f)
            return;

        _uiRefreshAccumulator = 0f;

        var closedUis = new List<EntityUid>();
        foreach (var (uid, accountId) in _openUiAccounts)
        {
            if (!_ui.IsUiOpen(uid, EconomyAccountUiKey.Key))
            {
                closedUis.Add(uid);
                continue;
            }

            if (string.IsNullOrWhiteSpace(accountId) || !_bank.TryFindAccountById(accountId, out var account))
            {
                SetWalletState(uid, accountId);
                continue;
            }

            SetWalletState(uid, accountId);
        }

        foreach (var uid in closedUis)
        {
            _openUiAccounts.Remove(uid);
        }
    }

    private void OnOwnedEntityParentChanged(ref EntParentChangedMessage args)
    {
        // Only track owned entities that have a mind; skip container/held items.
        if (!TryComp(args.Entity, out MindContainerComponent? mindContainer) || mindContainer.Mind is not { } mindUid)
            return;

        // Read-only: never create accounts for unregistered minds here.
        if (!TryComp(mindUid, out EconomyAccountComponent? account))
            return;

        if (_station.GetOwningStation(args.Entity) is not { } stationUid)
            return;

        if (account.OwningStation == stationUid)
            return;

        account.OwningStation = stationUid;
    }


    private void OnMindAdded(Entity<MindContainerComponent> ent, ref MindAddedMessage args)
    {
        if (!IsHumanoidMind(args.Mind.Comp))
            return;

        var account = _bank.EnsureAccount(args.Mind.Owner, args.Mind.Comp);

        if (args.Mind.Comp.OwnedEntity is { } owned && _station.GetOwningStation(owned) is { } stationUid)
            account.OwningStation = stationUid;

        EnsureStartingPayroll(args.Mind.Owner, args.Mind.Comp, account);

        if (!_idCard.TryFindIdCard(ent, out var idCard))
            return;

        if (idCard.Comp.BankAccountId == account.AccountId)
            return;

        idCard.Comp.BankAccountId = account.AccountId;
        Dirty(idCard);
    }

    private void OnRoleAdded(RoleAddedEvent args)
    {
        // VG-Tweak: only humanoid minds get a wallet and starting payroll;
        // silicon/ghost minds must not end up with a bank account.
        if (!IsHumanoidMind(args.Mind))
            return;

        var account = _bank.EnsureAccount(args.MindId, args.Mind);
        EnsureStartingPayroll(args.MindId, args.Mind, account);
    }

    /// <summary>
    ///     Pays the job's salary once when the player first joins so a freshly
    ///     spawned crew member is never completely broke.
    /// </summary>
    private void EnsureStartingPayroll(EntityUid mindUid, MindComponent mind, EconomyAccountComponent account)
    {
        if (account.StartingPayrollReceived || !_jobs.MindTryGetJob(mindUid, out var job) || job.Salary is not > 0)
            return;

        if (!_bank.Deposit((mindUid, account), job.Salary.Value, "starting-payroll", reasonData: job.ID))
            return;

        account.JobId ??= job.ID;
        account.StartingPayrollReceived = true;
    }


    private void OnUiOpened(Entity<IdCardComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (!ResolveAccount(ent, user, out var account))
        {
            SetWalletState(ent.Owner, ent.Comp.BankAccountId);
            return;
        }

        _openUiAccounts[ent.Owner] = account.Comp.AccountId;
        SetWalletState(ent.Owner, account.Comp.AccountId);
    }

    private void OnUiClosed(Entity<IdCardComponent> ent, ref BoundUIClosedEvent args)
    {
        _openUiAccounts.Remove(ent.Owner);
    }

    private void OnWithdrawMessage(Entity<IdCardComponent> ent, ref EconomyWithdrawMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (args.Amount <= 0 || !ResolveAccount(ent, user, out var account, args.AccountIdOverride))
            return;

        if (!_bank.Withdraw(account, args.Amount, "card-withdraw", GetNetEntity(user)))
        {
            _popup.PopupEntity(Loc.GetString("economy-card-withdraw-failed"), user, user);
            return;
        }

        var cash = _stack.SpawnAtPosition(args.Amount, CreditStackId, Transform(user).Coordinates);
        _hands.PickupOrDrop(user, cash);

        _openUiAccounts[ent.Owner] = account.Comp.AccountId;
        SetWalletState(ent.Owner, account.Comp.AccountId);
    }

    private void OnSelectAccountMessage(Entity<IdCardComponent> ent, ref EconomySelectAccountMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (!ResolveAccount(ent, user, out var account, args.AccountIdOverride))
        {
            var accountId = string.IsNullOrWhiteSpace(args.AccountIdOverride)
                ? ent.Comp.BankAccountId
                : args.AccountIdOverride.Trim();

            _openUiAccounts[ent.Owner] = accountId ?? string.Empty;
            SetWalletState(ent.Owner, accountId);
            return;
        }

        _openUiAccounts[ent.Owner] = account.Comp.AccountId;
        SetWalletState(ent.Owner, account.Comp.AccountId);
    }


    /// <summary>
    ///     A cash stack is clicked onto an ID card: deposit the whole stack.
    /// </summary>
    private void OnCashUsedOnCard(Entity<StackComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { Valid: true } target)
            return;

        if (ent.Comp.StackTypeId != CreditStackId || ent.Comp.Count <= 0)
            return;

        if (!HasComp<IdCardComponent>(target))
            return;

        var card = (target, Comp<IdCardComponent>(target));
        args.Handled = TryDepositStackToCard(card, args.User, ent, ent.Comp);
    }

    /// <summary>
    ///     An ID card is clicked onto a cash stack: deposit the whole stack.
    /// </summary>
    private void OnCardUsedOnCash(Entity<IdCardComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { Valid: true } target)
            return;

        if (!TryComp(target, out StackComponent? targetStack) || targetStack.Count <= 0)
            return;

        if (targetStack.StackTypeId != CreditStackId)
            return;

        args.Handled = TryDepositStackToCard(ent, args.User, target, targetStack);
    }

    private bool TryDepositStackToCard(Entity<IdCardComponent> card, EntityUid user, EntityUid stackUid, StackComponent stack)
    {
        if (!ResolveAccount(card, user, out var account))
            return false;

        // VG-Tweak: capture the original stack count, clear the stack first and
        // verify it was actually reduced before crediting the account.
        var amount = stack.Count;
        if (amount <= 0)
            return false;

        _stack.SetCount(stackUid, 0, stack);
        if (stack.Count == amount)
            return false;

        if (!_bank.Deposit(account, amount, "card-deposit", GetNetEntity(user)))
        {
            // Roll back the stack so no cash vanishes on a failed deposit.
            _stack.SetCount(stackUid, amount, stack);
            return false;
        }

        _openUiAccounts[card.Owner] = account.Comp.AccountId;
        SetWalletState(card.Owner, account.Comp.AccountId);
        return true;
    }

    private bool ResolveAccount(Entity<IdCardComponent> card, EntityUid user, out Entity<EconomyAccountComponent> account, string? accountOverride = null)
    {
        account = default;

        if (!_mind.TryGetMind(user, out _, out _))
            return false;

        var accountId = string.IsNullOrWhiteSpace(accountOverride)
            ? card.Comp.BankAccountId
            : accountOverride.Trim();

        if (string.IsNullOrWhiteSpace(accountId))
            return false;

        // VG-Tweak: a client-supplied account id may only ever select the acting
        // player's own account, so a spoofed id cannot view or draw from someone
        // else's balance. The card's own binding is used for legitimate cash
        // deposits regardless of who physically holds the card.
        if (!string.IsNullOrWhiteSpace(accountOverride))
        {
            if (!_bank.TryGetPlayerAccount(user, out var ownMind, out var ownAccount))
                return false;

            account = (ownMind, ownAccount);
            return true;
        }

        return _bank.TryFindAccountById(accountId, out account);
    }

    private bool IsHumanoidMind(MindComponent mind)
    {
        return mind.OwnedEntity is { } owned && HasComp<HumanoidProfileComponent>(owned);
    }

    /// <summary>
    ///     Sends the wallet state for an account id, including the newest
    ///     transactions so the client can render the history.
    /// </summary>
    private void SetWalletState(EntityUid uid, string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId) || !_bank.TryFindAccountById(accountId, out var account))
        {
            _ui.SetUiState(uid, EconomyAccountUiKey.Key, new EconomyAccountBoundUiState(accountId, 0));
            return;
        }

        var transactions = account.Comp.History
            .TakeLast(20)
            .Reverse()
            .Select(transaction => new EconomyUiTransaction(transaction.Time, transaction.Delta, transaction.Reason, transaction.ReasonData))
            .ToList();

        _ui.SetUiState(uid, EconomyAccountUiKey.Key,
            new EconomyAccountBoundUiState(account.Comp.AccountId, account.Comp.Balance, transactions));
    }
}

