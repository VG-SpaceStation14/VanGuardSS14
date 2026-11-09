using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.Chasm;
using Content.Shared.Climbing.Events;
using Content.Shared.Cuffs.Components;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Pulling.Events;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared._VanGuard.Carrying;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._VanGuard.Carrying;

public sealed partial class CarryingSystem
{
    private void OnCarryModeMobStateChanged(Entity<CarryModeComponent> mode, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        mode.Comp.Enabled = false;
        Dirty(mode.Owner, mode.Comp);
        _alerts.ClearAlert(mode.Owner, mode.Comp.Alert);
    }

    private void OnCarryModePlayerDetached(Entity<CarryModeComponent> mode, ref PlayerDetachedEvent args)
    {
        mode.Comp.Enabled = false;
        Dirty(mode.Owner, mode.Comp);
        _alerts.ClearAlert(mode.Owner, mode.Comp.Alert);
    }

    private void OnCarryModeMindRemoved(Entity<CarryModeComponent> mode, ref MindRemovedMessage args)
    {
        mode.Comp.Enabled = false;
        Dirty(mode.Owner, mode.Comp);
        _alerts.ClearAlert(mode.Owner, mode.Comp.Alert);
    }

    private void OnCarryAttemptStunned(Entity<CarryAttemptComponent> attempt, ref StunnedEvent args)
    {
        CancelAttempt(attempt.Owner);
    }

    private void OnCarryAttemptKnockedDownEvent(Entity<CarryAttemptComponent> attempt, ref KnockedDownEvent args)
    {
        CancelAttempt(attempt.Owner);
    }

    private void OnCarryAttemptCarrierTerminating(Entity<CarryAttemptComponent> attempt, ref EntityTerminatingEvent args)
    {
        ClearAttempt(attempt.Owner);
    }

    private void OnCarryAttemptShutdown(Entity<CarryAttemptComponent> attempt, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(attempt.Comp.Target) &&
            TryComp<CarryAttemptTargetComponent>(attempt.Comp.Target, out var targetAttempt) &&
            targetAttempt.Carrier == attempt.Owner)
        {
            RemComp<CarryAttemptTargetComponent>(attempt.Comp.Target);
        }
    }

    private void OnCarryAttemptPlayerDetached(Entity<CarryAttemptComponent> attempt, ref PlayerDetachedEvent args)
    {
        CancelAttempt(attempt.Owner);
    }

    private void OnCarryAttemptMindRemoved(Entity<CarryAttemptComponent> attempt, ref MindRemovedMessage args)
    {
        CancelAttempt(attempt.Owner);
    }

    private void OnCarryAttemptMobStateChanged(Entity<CarryAttemptComponent> attempt, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            CancelAttempt(attempt.Owner);
    }

    private void OnCarryAttemptTargetPlayerDetached(Entity<CarryAttemptTargetComponent> target,
        ref PlayerDetachedEvent args)
    {
        if (!TerminatingOrDeleted(target.Comp.Carrier))
            CancelAttempt(target.Comp.Carrier);
    }

    private void OnCarryAttemptTargetMindRemoved(Entity<CarryAttemptTargetComponent> target,
        ref MindRemovedMessage args)
    {
        if (!TerminatingOrDeleted(target.Comp.Carrier))
            CancelAttempt(target.Comp.Carrier);
    }

    private void OnCarryAttemptTargetMobStateChanged(Entity<CarryAttemptTargetComponent> target,
        ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead && !TerminatingOrDeleted(target.Comp.Carrier))
            CancelAttempt(target.Comp.Carrier);
    }

    private void OnCarryAttemptTargetTerminating(Entity<CarryAttemptTargetComponent> target, ref EntityTerminatingEvent args)
    {
        if (!TerminatingOrDeleted(target.Comp.Carrier) &&
            TryComp<CarryAttemptComponent>(target.Comp.Carrier, out var attempt))
        {
            if (attempt.DoAfter is { } doAfter)
                _doAfter.Cancel(doAfter);
            ClearAttempt(target.Comp.Carrier);
        }
    }

    private void OnCarrierStunnedEvent(Entity<CarryingComponent> carrier, ref StunnedEvent args)
    {
        EndCarryForState(carrier.Owner);
    }

    private void OnCarrierKnockedDownEvent(Entity<CarryingComponent> carrier, ref KnockedDownEvent args)
    {
        EndCarryForState(carrier.Owner);
    }

    private void OnCarrierSleeping(Entity<SleepingComponent> sleeping, ref ComponentStartup args)
    {
        EndCarryForState(sleeping.Owner);
        CancelAttempt(sleeping.Owner);
    }

    private void OnCarrierFalling(Entity<ChasmFallingComponent> falling, ref ComponentStartup args)
    {
        EndCarryForState(falling.Owner);
        CancelAttempt(falling.Owner);
    }

    private void OnCarrierCuffed(Entity<CuffableComponent> cuffed, ref CuffedStateChangeEvent args)
    {
        // Cuffing only blocks carrying when it leaves the carrier unable to
        // interact. Species with enough spare hands may keep holding.
        if (cuffed.Comp.CanStillInteract)
            return;

        EndCarryForState(cuffed.Owner);
        CancelAttempt(cuffed.Owner);
    }

    private void EndCarryForState(EntityUid carrier)
    {
        if (!TryComp<CarryingComponent>(carrier, out var carrying))
            return;

        EndCarry(carrier, carrying.Carried, carrier, "carry-interrupted");
    }

    private void CancelAttempt(EntityUid carrier)
    {
        if (!TryComp<CarryAttemptComponent>(carrier, out var attempt))
            return;

        if (attempt.DoAfter is { } doAfter)
            _doAfter.Cancel(doAfter);

        ClearAttempt(carrier);
        Popup(carrier, "carry-interrupted");
    }

    private void ClearAttempt(EntityUid carrier, EntityUid? expectedTarget = null)
    {
        if (!TryComp<CarryAttemptComponent>(carrier, out var attempt))
            return;

        if (expectedTarget is { } expected && attempt.Target != expected)
            return;

        if (!TerminatingOrDeleted(attempt.Target) &&
            TryComp<CarryAttemptTargetComponent>(attempt.Target, out var targetAttempt) &&
            targetAttempt.Carrier == carrier)
        {
            RemComp<CarryAttemptTargetComponent>(attempt.Target);
        }

        RemComp<CarryAttemptComponent>(carrier);
    }

    private void OnCarrierMobStateChanged(Entity<CarryingComponent> carrier, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        DisableCarryMode(carrier.Owner);
        EndCarry(carrier.Owner, carrier.Comp.Carried, carrier.Owner, "carry-dropped-dead");
    }

    private void OnCarriedMobStateChanged(Entity<CarriedComponent> carried, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || carried.Comp.Carrier is not { } carrier)
            return;

        EndCarry(carrier, carried.Owner, carrier, "carry-dropped-dead");
    }

    private void OnCarrierTerminating(Entity<CarryingComponent> carrier, ref EntityTerminatingEvent args)
    {
        EndCarry(carrier.Owner, carrier.Comp.Carried, carrier.Owner, null);
    }

    private void OnCarriedTerminating(Entity<CarriedComponent> carried, ref EntityTerminatingEvent args)
    {
        if (carried.Comp.Carrier is not { } carrier)
            return;

        EndCarry(carrier, carried.Owner, carrier, null);
    }

    private void OnAnchorTerminating(Entity<CarryAnchorComponent> anchor, ref EntityTerminatingEvent args)
    {
        var carrier = anchor.Comp.Carrier;
        if (carrier == EntityUid.Invalid)
            return;

        // The anchor is already going away. EndCarry will still remove the
        // carried state and leave the target on the map.
        EndCarry(carrier, null, carrier, null);
    }

    private void OnCarrierPlayerDetached(Entity<CarryingComponent> carrier, ref PlayerDetachedEvent args)
    {
        EndCarry(carrier.Owner, carrier.Comp.Carried, carrier.Owner, "carry-dropped-dead");
    }

    private void OnCarriedPlayerDetached(Entity<CarriedComponent> carried, ref PlayerDetachedEvent args)
    {
        if (carried.Comp.Carrier is not { } carrier)
            return;

        EndCarry(carrier, carried.Owner, carrier, "carry-dropped-dead");
    }

    private void OnCarrierMindRemoved(Entity<CarryingComponent> carrier, ref MindRemovedMessage args)
    {
        EndCarry(carrier.Owner, carrier.Comp.Carried, carrier.Owner, "carry-dropped-dead");
    }

    private void OnCarriedMindRemoved(Entity<CarriedComponent> carried, ref MindRemovedMessage args)
    {
        if (carried.Comp.Carrier is not { } carrier)
            return;

        EndCarry(carrier, carried.Owner, carrier, "carry-dropped-dead");
    }

    private void OnCarrierBeforeClimb(Entity<CarryingComponent> carrier, ref SelfBeforeClimbEvent args)
    {
        if (args.GettingPutOnTable == carrier.Owner)
            args.Cancel();
    }

    private void OnCarrierContainerInsertAttempt(Entity<CarryingComponent> carrier, ref ContainerGettingInsertedAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnCarriedContainerInsertAttempt(Entity<CarriedComponent> carried, ref ContainerGettingInsertedAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnCarrierBuckleAttempt(Entity<CarryingComponent> carrier, ref BuckleAttemptEvent args)
    {
        args.Cancelled = true;
        if (args.Popup && args.User is { } user)
            Popup(user, "carry-mode-busy");
    }

    private void OnCarrierDowned(Entity<CarryingComponent> carrier, ref DownedEvent args)
    {
        EndCarryForState(carrier.Owner);
    }

    private void OnCarrierStartPullAttempt(Entity<CarryingComponent> carrier, ref StartPullAttemptEvent args)
    {
        if (args.Puller == carrier.Owner)
            args.Cancel();
    }

    private void OnCarrierBeingPulledAttempt(Entity<CarryingComponent> carrier, ref BeingPulledAttemptEvent args)
    {
        if (args.Pulled == carrier.Owner)
            args.Cancel();
    }

    private void OnCarrierVirtualItemDeleted(Entity<CarryingComponent> carrier, ref VirtualItemDeletedEvent args)
    {
        if (carrier.Comp.Carried == args.BlockingEntity)
            EndCarry(carrier.Owner, carrier.Comp.Carried, carrier.Owner, "carry-dropped-dead");
    }

    private void OnCarriedShutdown(Entity<CarriedComponent> carried, ref ComponentShutdown args)
    {
        if (carried.Comp.Ending || carried.Comp.Carrier is not { } carrier)
            return;

        // The Carried component may be gone by the time EndCarry runs, so
        // restore the visual value before releasing the rest of the state.
        RestoreHorizontalRotation(carried.Owner, carried.Comp.OriginalHorizontalRotation);
        EndCarry(carrier, carried.Owner, carrier, "carry-dropped-dead", removeCarried: false);
    }

    private void OnCarriedUpdateCanMove(Entity<CarriedComponent> carried, ref UpdateCanMoveEvent args)
    {
        args.Cancel();
    }

    private void OnCarriedStandAttempt(Entity<CarriedComponent> carried, ref StandAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnCarriedStartPullAttempt(Entity<CarriedComponent> carried, ref StartPullAttemptEvent args)
    {
        if (args.Puller == carried.Owner)
            args.Cancel();
    }

    private void OnCarriedBeingPulledAttempt(Entity<CarriedComponent> carried, ref BeingPulledAttemptEvent args)
    {
        if (args.Pulled == carried.Owner)
            args.Cancel();
    }

    private void OnCarriedInteractionAttempt(Entity<CarriedComponent> carried, ref InteractionAttemptEvent args)
    {
        if (args.Uid != carried.Owner)
            return;

        if (carried.Comp.Carrier is { } carrier && args.Target == carrier)
            return;

        args.Cancelled = true;
    }

    private void OnCarriedBeforeClimb(Entity<CarriedComponent> carried, ref SelfBeforeClimbEvent args)
    {
        if (args.GettingPutOnTable == carried.Owner)
            args.Cancel();
    }

    private void OnCarriedUnbuckled(Entity<CarriedComponent> carried, ref UnbuckledEvent args)
    {
        if (carried.Comp.Ending)
            return;

        if (carried.Comp.Carrier is not { } carrier)
            return;

        EndCarry(carrier, carried.Owner, carrier, "carry-dropped-dead");
    }

    private void OnCarriedUnbuckleAttempt(Entity<CarriedComponent> carried, ref UnbuckleAttemptEvent args)
    {
        if (args.User == carried.Owner)
            args.Cancelled = true;
    }
}
