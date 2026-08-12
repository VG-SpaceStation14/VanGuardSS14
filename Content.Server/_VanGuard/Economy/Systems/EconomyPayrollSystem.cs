using Content.Server.Cargo.Systems;
using Content.Server.Popups;
using Content.Server._VanGuard.Economy.Components;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.Mind;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._VanGuard.Economy.Systems;

/// <summary>
///     Periodically pays every employed mind its job salary. When the job is paid
///     from the station budget the money is deducted from the station cargo account
///     first, so a bankrupt station stops paying until it earns credits again.
/// </summary>
public sealed partial class EconomyPayrollSystem : EntitySystem
{
    [Dependency] private EconomyBankSystem _bank = default!;
    [Dependency] private CargoSystem _cargo = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    private readonly ISawmill _sawmill = Logger.GetSawmill("economy-payroll");

    private static readonly SoundSpecifier PayrollSound = new SoundPathSpecifier("/Audio/Machines/beep.ogg");

    public void ProcessPayroll()
    {
        var query = EntityQueryEnumerator<MindComponent>();
        while (query.MoveNext(out var mindUid, out var mindComp))
        {
            if (mindComp.UserId == null || mindComp.OwnedEntity is not { } owned)
                continue;

            var account = _bank.EnsureAccount(mindUid, mindComp);
            if (!_jobs.MindTryGetJob(mindUid, out var job) || job.Salary is not > 0)
                continue;

            var paid = job.Salary.Value;
            var stationUid = _station.GetOwningStation(owned);

            if (job.PayrollFromStationBudget && stationUid != null
                && TryComp<StationBankAccountComponent>(stationUid, out var stationBank))
            {
                var primaryAccount = stationBank.PrimaryAccount;
                var budget = _cargo.GetBalanceFromAccount((stationUid.Value, stationBank), primaryAccount);
                if (budget <= 0)
                    continue;

                paid = Math.Min(paid, budget);
                _cargo.UpdateBankAccount((stationUid.Value, stationBank), -paid, primaryAccount);
            }

            account.JobId = job.ID;

            var deposited = stationUid != null
                ? _bank.Deposit((mindUid, account), paid, "payroll", GetNetEntity(stationUid.Value), job.ID)
                : _bank.Deposit((mindUid, account), paid, "payroll", reasonData: job.ID);

            if (!deposited)
            {
                _sawmill.Error($"Payroll deposit failed for job {job.ID} recipient {mindUid} amount {paid}.");
                continue;
            }

            NotifyPayroll(owned, account.AccountId, paid);
        }
    }

    private void NotifyPayroll(EntityUid recipient, string accountId, int amount)
    {
        if (!_idCard.TryFindIdCard(recipient, out var idCard))
            return;

        if (idCard.Comp.BankAccountId != accountId)
        {
            idCard.Comp.BankAccountId = accountId;
            Dirty(idCard);
        }

        var popupText = Loc.GetString("payroll-popup-received", ("amount", amount));
        _popup.PopupEntity(popupText, recipient, recipient);
        _audio.PlayPvs(PayrollSound, Transform(recipient).Coordinates, AudioParams.Default.WithVolume(-2f));
    }
}
