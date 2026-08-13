using System.Linq;
using Content.Shared.Cargo.Components;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Emag.Systems;
using Content.Shared.UserInterface;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    private bool _allowPrimaryAccountAllocation;
    private bool _allowPrimaryCutAdjustment;

    public void InitializeFunds()
    {
        SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleWithdrawFundsMessage>(OnWithdrawFunds);
        SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleToggleLimitMessage>(OnToggleLimit);
        // VG-Tweak Start: station balance deposit/withdraw from the card console.
        SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleStationFundsMessage>(OnStationFunds);
        // VG-Tweak End
        SubscribeLocalEvent<FundingAllocationConsoleComponent, SetFundingAllocationBuiMessage>(OnSetFundingAllocation);
        SubscribeLocalEvent<FundingAllocationConsoleComponent, BeforeActivatableUIOpenEvent>(OnFundAllocationBuiOpen);

        _cfg.OnValueChanged(CCVars.AllowPrimaryAccountAllocation, enabled => { _allowPrimaryAccountAllocation = enabled; }, true);
        _cfg.OnValueChanged(CCVars.AllowPrimaryCutAdjustment, enabled => { _allowPrimaryCutAdjustment = enabled; }, true);
    }

    /// <summary>
    ///     VG-Tweak: lets a crew member deposit credits from their personal bank
    ///     account into the station budget, or withdraw station funds onto their
    ///     account. Withdrawals of large sums require console access.
    /// </summary>
    private void OnStationFunds(Entity<CargoOrderConsoleComponent> ent, ref CargoConsoleStationFundsMessage args)
    {
        if (args.Actor is not { Valid: true } actor || args.Amount <= 0)
            return;

        if (_station.GetOwningStation(ent) is not { } station ||
            !TryComp<StationBankAccountComponent>(station, out var bank))
        {
            _popup.PopupCursor(Loc.GetString("cargo-console-station-not-found"), actor);
            return;
        }

        var primaryAccount = bank.PrimaryAccount;

        switch (args.Action)
        {
            case CargoStationFundsAction.Deposit:
                if (!_bank.TryGetPlayerAccount(actor, out var mindUid, out var account))
                {
                    _popup.PopupCursor(Loc.GetString("cargo-console-no-account"), actor);
                    PlayDenySound(ent, ent.Comp);
                    return;
                }

                if (!_bank.Withdraw((mindUid, account), args.Amount, "station-deposit", GetNetEntity(station), primaryAccount))
                {
                    _popup.PopupCursor(Loc.GetString("cargo-console-station-insufficient-funds"), actor);
                    PlayDenySound(ent, ent.Comp);
                    return;
                }

                UpdateBankAccount((station, bank), args.Amount, primaryAccount);
                _audio.PlayPvs(ApproveSound, ent);
                _adminLogger.Add(LogType.Action, LogImpact.Medium,
                    $"{ToPrettyString(actor):player} deposited {args.Amount} credits into station {ToPrettyString(station)} from their bank account.");
                break;

            case CargoStationFundsAction.Withdraw:
                // Withdrawals always require console access so a random crew
                // member cannot drain the station budget into their account.
                if (!_accessReaderSystem.IsAllowed(actor, ent))
                {
                    _popup.PopupCursor(Loc.GetString("cargo-console-order-not-allowed"), actor);
                    PlayDenySound(ent, ent.Comp);
                    return;
                }

                var available = GetBalanceFromAccount((station, bank), primaryAccount);
                if (args.Amount > available)
                {
                    _popup.PopupCursor(Loc.GetString("cargo-console-insufficient-station-funds"), actor);
                    PlayDenySound(ent, ent.Comp);
                    return;
                }

                if (!_bank.TryGetPlayerAccount(actor, out mindUid, out account))
                {
                    _popup.PopupCursor(Loc.GetString("cargo-console-no-account"), actor);
                    PlayDenySound(ent, ent.Comp);
                    return;
                }

                UpdateBankAccount((station, bank), -args.Amount, primaryAccount);
                _bank.Deposit((mindUid, account), args.Amount, "station-withdraw", GetNetEntity(station), primaryAccount);
                _audio.PlayPvs(ApproveSound, ent);
                _adminLogger.Add(LogType.Action, LogImpact.Medium,
                    $"{ToPrettyString(actor):player} withdrew {args.Amount} credits from station {ToPrettyString(station)}.");
                break;
        }
    }

    private void OnWithdrawFunds(Entity<CargoOrderConsoleComponent> ent, ref CargoConsoleWithdrawFundsMessage args)
    {
        if (_station.GetOwningStation(ent) is not { } station ||
            !TryComp<StationBankAccountComponent>(station, out var bank))
            return;

        if (args.Account == ent.Comp.Account ||
            args.Amount <= 0 ||
            args.Amount > GetBalanceFromAccount((station, bank), ent.Comp.Account) * ent.Comp.TransferLimit)
            return;

        if (Timing.CurTime < ent.Comp.NextAccountActionTime)
            return;

        if (!_accessReaderSystem.IsAllowed(args.Actor, ent))
        {
            _popup.PopupCursor(Loc.GetString("cargo-console-order-not-allowed"), args.Actor);
            PlayDenySound(ent, ent.Comp);
            return;
        }

        ent.Comp.NextAccountActionTime = Timing.CurTime + ent.Comp.AccountActionDelay;
        UpdateBankAccount((station, bank), -args.Amount, ent.Comp.Account, dirty: false);
        _audio.PlayPvs(ApproveSound, ent);

        var ourAccount = ProtoMan.Index(ent.Comp.Account);
        var name = _identity.GetIdentityShortInfo(args.Actor, ent)
                   ?? Loc.GetString("cargo-console-fund-transfer-user-unknown");
        if (args.Account == null)
        {
            var stackPrototype = ProtoMan.Index(ent.Comp.CashType);
            _stack.SpawnAtPosition(args.Amount, stackPrototype, Transform(ent).Coordinates);

            if (!_emag.CheckFlag(ent, EmagType.Interaction))
            {
                var msg = Loc.GetString("cargo-console-fund-withdraw-broadcast",
                    ("name", name),
                    ("amount", args.Amount),
                    ("name1", Loc.GetString(ourAccount.Name)),
                    ("code1", Loc.GetString(ourAccount.Code)));
                _radio.SendRadioMessage(ent, msg, ourAccount.RadioChannel, ent, escapeMarkup: false);
            }
        }
        else
        {
            var otherAccount = ProtoMan.Index(args.Account.Value);
            UpdateBankAccount((station, bank), args.Amount, args.Account.Value);

            if (!_emag.CheckFlag(ent, EmagType.Interaction))
            {
                var msg = Loc.GetString("cargo-console-fund-transfer-broadcast",
                    ("name", name),
                    ("amount", args.Amount),
                    ("name1", Loc.GetString(ourAccount.Name)),
                    ("code1", Loc.GetString(ourAccount.Code)),
                    ("name2", Loc.GetString(otherAccount.Name)),
                    ("code2", Loc.GetString(otherAccount.Code)));
                _radio.SendRadioMessage(ent, msg, ourAccount.RadioChannel, ent, escapeMarkup: false);
                _radio.SendRadioMessage(ent, msg, otherAccount.RadioChannel, ent, escapeMarkup: false);
            }
        }
    }

    private void OnToggleLimit(Entity<CargoOrderConsoleComponent> ent, ref CargoConsoleToggleLimitMessage args)
    {
        if (!_accessReaderSystem.FindAccessTags(args.Actor).Intersect(ent.Comp.RemoveLimitAccess).Any())
        {
            _popup.PopupCursor(Loc.GetString("cargo-console-order-not-allowed"), args.Actor);
            PlayDenySound(ent, ent.Comp);
            return;
        }

        _audio.PlayPvs(ent.Comp.ToggleLimitSound, ent);
        ent.Comp.TransferUnbounded = !ent.Comp.TransferUnbounded;
        Dirty(ent);
    }


    private void OnSetFundingAllocation(Entity<FundingAllocationConsoleComponent> ent, ref SetFundingAllocationBuiMessage args)
    {
        if (_station.GetOwningStation(ent) is not { } station ||
            !TryComp<StationBankAccountComponent>(station, out var bank))
            return;

        var expectedCount = _allowPrimaryAccountAllocation ? bank.RevenueDistribution.Count : bank.RevenueDistribution.Count - 1;
        if (args.Percents.Count != expectedCount)
            return;

        var differs = false;
        foreach (var (account, percent) in args.Percents)
        {
            if (percent != (int) Math.Round(bank.RevenueDistribution[account] * 100))
            {
                differs = true;
                break;
            }
        }
        differs = differs || args.PrimaryCut != bank.PrimaryCut || args.LockboxCut != bank.LockboxCut;

        if (!differs)
            return;

        if (args.Percents.Values.Sum() != 100)
            return;

        var primaryCut = bank.RevenueDistribution[bank.PrimaryAccount];
        bank.RevenueDistribution.Clear();
        foreach (var (account, percent )in args.Percents)
        {
            bank.RevenueDistribution.Add(account, percent / 100.0);
        }
        if (!_allowPrimaryAccountAllocation)
        {
            bank.RevenueDistribution.Add(bank.PrimaryAccount, 0);
        }

        if (_allowPrimaryCutAdjustment && args.PrimaryCut is >= 0.0 and <= 1.0)
        {
            bank.PrimaryCut = args.PrimaryCut;
        }
        if (_lockboxCutEnabled && args.LockboxCut is >= 0.0 and <= 1.0)
        {
            bank.LockboxCut = args.LockboxCut;
        }

        Dirty(station, bank);

        _audio.PlayPvs(ent.Comp.SetDistributionSound, ent);
        _adminLogger.Add(
            LogType.Action,
            LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} set station {ToPrettyString(station)} fund distribution: {string.Join(',', bank.RevenueDistribution.Select(p => $"{p.Key}: {p.Value}").ToList())}, primary cut: {bank.PrimaryCut}, lockbox cut: {bank.LockboxCut}");
    }

    private void OnFundAllocationBuiOpen(Entity<FundingAllocationConsoleComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        if (_station.GetOwningStation(ent) is { } station)
            _uiSystem.SetUiState(ent.Owner, FundingAllocationConsoleUiKey.Key, new FundingAllocationConsoleBuiState(GetNetEntity(station)));
    }
}
