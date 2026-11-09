using Content.Shared.Buckle.Components;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.GameObjects;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared._VanGuard.Carrying;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Server._VanGuard.Carrying;

public sealed partial class CarryingSystem
{
    private void OnCarryKey(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { Valid: true } carrier)
            return;

        TryToggleCarryMode(carrier);
    }

    public bool TryToggleCarryMode(EntityUid carrier)
    {
        if (TerminatingOrDeleted(carrier) ||
            !HasComp<HumanoidProfileComponent>(carrier) ||
            !TryComp<MobStateComponent>(carrier, out var mob) ||
            !_mobState.IsAlive(carrier, mob) ||
            !TryComp<Content.Shared.Hands.Components.HandsComponent>(carrier, out var hands) ||
            CountUsableFreeHands(carrier, hands) < 2)
        {
            Popup(carrier, "carry-not-conscious");
            return false;
        }

        if (HasComp<CarryingComponent>(carrier) || HasComp<CarriedComponent>(carrier) ||
            HasComp<CarryAttemptComponent>(carrier) || !_blocker.CanInteract(carrier, null))
        {
            Popup(carrier, "carry-mode-busy");
            return false;
        }

        if (TryComp<BuckleComponent>(carrier, out var buckle) && buckle.Buckled ||
            TryComp<PullerComponent>(carrier, out var puller) && puller.Pulling != null ||
            TryComp<PullableComponent>(carrier, out var pullable) && pullable.Puller != null)
        {
            Popup(carrier, "carry-mode-busy");
            return false;
        }

        var mode = EnsureComp<CarryModeComponent>(carrier);
        mode.Enabled = !mode.Enabled;
        Dirty(carrier, mode);

        if (mode.Enabled)
        {
            _alerts.ShowAlert(carrier, mode.Alert);
            Popup(carrier, "carry-mode-enabled");
        }
        else
        {
            _alerts.ClearAlert(carrier, mode.Alert);
            Popup(carrier, "carry-mode-disabled");
        }

        return true;
    }

    private void OnTargetInteractHand(Entity<MobStateComponent> target, ref InteractHandEvent args)
    {
        if (args.Handled || args.User == target.Owner)
            return;

        if (!TryComp<CarryModeComponent>(args.User, out var mode) || !mode.Enabled)
            return;

        if (!HasComp<HumanoidProfileComponent>(target.Owner))
            return;

        args.Handled = true;
        TryStartCarry(args.User, target.Owner);
    }

    private void OnTargetVerbs(Entity<MobStateComponent> target, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.User == target.Owner)
            return;

        if (HasComp<CarriedComponent>(args.User) ||
            !HasComp<HumanoidProfileComponent>(args.User) ||
            !HasComp<HumanoidProfileComponent>(target.Owner))
            return;

        if (!CanStartCarry(args.User, target.Owner, out _))
            return;

        var user = args.User;
        var targetUid = target.Owner;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("carry-verb"),
            Act = () => TryStartCarry(user, targetUid),
            DoContactInteraction = false,
        });
    }

    private void OnCarrierVerbs(Entity<CarryingComponent> carrier, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.User != carrier.Owner)
            return;

        var carrierUid = carrier.Owner;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("carry-verb-drop"),
            Act = () => TryEndCarry(carrierUid, carrierUid),
            DoContactInteraction = false,
        });
    }

    private void OnCarryDoAfter(Entity<MobStateComponent> carrier, ref CarryDoAfterEvent args)
    {
        ClearAttempt(carrier.Owner, args.Target);

        if (args.Cancelled || args.Handled || args.Target is not { } target ||
            !Exists(target) || TerminatingOrDeleted(target))
        {
            if (!args.Cancelled && !args.Handled)
                Popup(args.User, "carry-interrupted");
            return;
        }

        if (!CanStartCarry(args.User, target, out var reason))
        {
            Popup(args.User, reason ?? "carry-failed", target);
            return;
        }

        args.Handled = true;
        TryStartCarryNow(args.User, target);
    }

    private void OnCarriedMoveInput(Entity<CarriedComponent> carried, ref MoveInputEvent args)
    {
        if (!args.HasDirectionalMovement || carried.Comp.Ending ||
            carried.Comp.Carrier is not { } carrier)
            return;

        var doAfter = new DoAfterArgs(EntityManager, carried.Owner, EscapeTime,
            new CarryEscapeDoAfterEvent(), carried.Owner, target: carried.Owner)
        {
            BreakOnMove = false,
            BreakOnDamage = true,
            NeedHand = false,
            RequireCanInteract = false,
            BlockDuplicate = true,
            CancelDuplicate = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            Popup(carried.Owner, "carry-escape-start");
    }

    private void OnCarryEscapeComplete(Entity<CarriedComponent> carried, ref CarryEscapeDoAfterEvent args)
    {
        if (args.Cancelled)
        {
            if (!carried.Comp.Ending)
                Popup(carried.Owner, "carry-escape-cancelled");
            return;
        }

        if (args.Handled || carried.Comp.Ending ||
            carried.Comp.Carrier is not { } carrier)
            return;

        args.Handled = true;
        EndCarry(carrier, carried.Owner, carried.Owner, "carry-escape-success");
    }

    private void OnCarryModeShutdown(Entity<CarryModeComponent> mode, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(mode.Owner))
            _alerts.ClearAlert(mode.Owner, mode.Comp.Alert);
    }
}
