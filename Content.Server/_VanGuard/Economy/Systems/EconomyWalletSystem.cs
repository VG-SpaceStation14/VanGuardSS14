using Content.Server.Access.Systems;
using Content.Server._VanGuard.Economy.Components;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared._VanGuard.Mind;
using Content.Shared.Roles.Jobs;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;

namespace Content.Server._VanGuard.Economy.Systems;

/// <summary>
///     Creates the single personal bank account owned by a mind and keeps the
///     holder's ID card linked to it. The card is an identifier/authorization
///     token only; it no longer opens a banking UI and never owns a balance.
/// </summary>
public sealed partial class EconomyWalletSystem : EntitySystem
{
    [Dependency] private EconomyBankSystem _bank = default!;
    [Dependency] private IdCardSystem _idCard = default!;
    [Dependency] private ServerStationSystem _station = default!;
    [Dependency] private SharedJobSystem _jobs = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MindContainerComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<RoleAddedEvent>(OnRoleAdded);
        SubscribeLocalEvent<EntParentChangedMessage>(OnOwnedEntityParentChanged);
    }

    private void OnOwnedEntityParentChanged(ref EntParentChangedMessage args)
    {
        if (!TryComp(args.Entity, out MindContainerComponent? mindContainer) || mindContainer.Mind is not { } mindUid)
            return;

        if (!TryComp(mindUid, out EconomyAccountComponent? account))
            return;

        if (_station.GetOwningStation(args.Entity) is not { } stationUid || account.OwningStation == stationUid)
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

        EnsureStartingBalance(args.Mind.Owner, args.Mind.Comp, account);

        if (!_idCard.TryFindIdCard(ent, out var idCard) || idCard.Comp.BankAccountId == account.AccountId)
            return;

        idCard.Comp.BankAccountId = account.AccountId;
        Dirty(idCard);
    }

    private void OnRoleAdded(RoleAddedEvent args)
    {
        if (!IsHumanoidMind(args.Mind))
            return;

        var account = _bank.EnsureAccount(args.MindId, args.Mind);
        EnsureStartingBalance(args.MindId, args.Mind, account);
    }

    /// <summary>
    ///     Pays one full job salary once, as the character's opening balance.
    ///     This is deliberately separate from the periodic payroll and is
    ///     guarded by a persisted account flag.
    /// </summary>
    private void EnsureStartingBalance(EntityUid mindUid, MindComponent mind, EconomyAccountComponent account)
    {
        EnsureBankMemory(mind, account);

        if (account.StartingBalancePaid)
            return;

        if (!_jobs.MindTryGetJob(mindUid, out var job) || job == null)
            return;

        account.StartingBalancePaid = true;
        account.StartingPayrollReceived = true;

        if (job.Salary is not > 0)
            return;

        _bank.Deposit((mindUid, account), job.Salary.Value, "starting-payroll", reasonData: job.ID);
    }

    private static void EnsureBankMemory(MindComponent mind, EconomyAccountComponent account)
    {
        mind.AddMemory(new Memory("character-info-memory-pin", account.Pin));
        mind.AddMemory(new Memory("character-info-memory-account", account.AccountId));
    }

    private bool IsHumanoidMind(MindComponent mind)
    {
        return mind.OwnedEntity is { } owned && HasComp<HumanoidProfileComponent>(owned);
    }
}
