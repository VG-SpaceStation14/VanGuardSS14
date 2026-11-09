using System.Linq;
using System.Numerics;
using Content.Shared.Climbing.Components;
using Content.Shared.Chasm;
using Content.Shared.Climbing.Events;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Movement.Events;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Verbs;
using Content.Server.Buckle.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Alert;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.Cuffs.Components;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Pulling.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared._VanGuard.Carrying;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Carrying;

/// <summary>
/// Server-authoritative character carrying.
///
/// The carried mob is attached through an invisible, temporary strap entity
/// using the existing buckle/transform/joint systems. This keeps the carried
/// mob on the carrier without duplicating transform or movement logic.
/// </summary>
public sealed partial class CarryingSystem : EntitySystem
{
    private const float CarryRange = 2f;
    private static readonly TimeSpan PickupTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EscapeTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EntryAnimationTime = TimeSpan.FromSeconds(0.25);
    private static readonly SoundPathSpecifier PickupSound = new("/Audio/Effects/thudswoosh.ogg");

    [Dependency] private BuckleSystem _buckle = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedVirtualItemSystem _virtual = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly EntProtoId CarryAnchor = "CarryAnchor";

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.CarryMode,
                InputCmdHandler.FromDelegate(OnCarryKey, handle: false))
            .BindBefore(ContentKeyFunctions.Drop,
                new PointerInputCmdHandler(OnCarryDropKey), typeof(SharedHandsSystem))
            .Register<CarryingSystem>();

        SubscribeLocalEvent<CarryingComponent, ComponentInit>(OnCarryingInit);
        SubscribeLocalEvent<CarryingComponent, ComponentShutdown>(OnCarryingShutdown);
        SubscribeLocalEvent<CarryingComponent, CarryDropAlertEvent>(OnCarryDropAlert);
        SubscribeAllEvent<CarryDropRequestEvent>(OnCarryDropRequest);
        SubscribeLocalEvent<CarryingComponent, RefreshMovementSpeedModifiersEvent>(OnCarrierRefreshSpeed);
        SubscribeLocalEvent<CarryingComponent, GetVerbsEvent<InteractionVerb>>(OnCarrierVerbs);
        SubscribeLocalEvent<CarryingComponent, MobStateChangedEvent>(OnCarrierMobStateChanged);
        SubscribeLocalEvent<CarryingComponent, EntityTerminatingEvent>(OnCarrierTerminating);
        SubscribeLocalEvent<CarryingComponent, PlayerDetachedEvent>(OnCarrierPlayerDetached);
        SubscribeLocalEvent<CarryingComponent, MindRemovedMessage>(OnCarrierMindRemoved);
        SubscribeLocalEvent<CarryingComponent, BuckleAttemptEvent>(OnCarrierBuckleAttempt);
        SubscribeLocalEvent<CarryingComponent, StartPullAttemptEvent>(OnCarrierStartPullAttempt);
        SubscribeLocalEvent<CarryingComponent, BeingPulledAttemptEvent>(OnCarrierBeingPulledAttempt);
        SubscribeLocalEvent<CarryingComponent, VirtualItemDeletedEvent>(OnCarrierVirtualItemDeleted);
        SubscribeLocalEvent<CarryingComponent, StunnedEvent>(OnCarrierStunnedEvent);
        SubscribeLocalEvent<CarryingComponent, KnockedDownEvent>(OnCarrierKnockedDownEvent);
        SubscribeLocalEvent<CarryingComponent, DownedEvent>(OnCarrierDowned);
        SubscribeLocalEvent<SleepingComponent, ComponentStartup>(OnCarrierSleeping);
        SubscribeLocalEvent<ChasmFallingComponent, ComponentStartup>(OnCarrierFalling);
        SubscribeLocalEvent<CuffableComponent, CuffedStateChangeEvent>(OnCarrierCuffed);
        SubscribeLocalEvent<CarryingComponent, ContainerGettingInsertedAttemptEvent>(OnCarrierContainerInsertAttempt);
        SubscribeLocalEvent<CarryingComponent, SelfBeforeClimbEvent>(OnCarrierBeforeClimb);
        SubscribeLocalEvent<CarriedComponent, ContainerGettingInsertedAttemptEvent>(OnCarriedContainerInsertAttempt);

        SubscribeLocalEvent<CarriedComponent, ComponentShutdown>(OnCarriedShutdown);
        SubscribeLocalEvent<CarriedComponent, MobStateChangedEvent>(OnCarriedMobStateChanged);
        SubscribeLocalEvent<CarriedComponent, EntityTerminatingEvent>(OnCarriedTerminating);
        SubscribeLocalEvent<CarriedComponent, PlayerDetachedEvent>(OnCarriedPlayerDetached);
        SubscribeLocalEvent<CarriedComponent, MindRemovedMessage>(OnCarriedMindRemoved);
        SubscribeLocalEvent<CarriedComponent, UpdateCanMoveEvent>(OnCarriedUpdateCanMove);
        SubscribeLocalEvent<CarriedComponent, StandAttemptEvent>(OnCarriedStandAttempt);
        SubscribeLocalEvent<CarriedComponent, StartPullAttemptEvent>(OnCarriedStartPullAttempt);
        SubscribeLocalEvent<CarriedComponent, BeingPulledAttemptEvent>(OnCarriedBeingPulledAttempt);
        SubscribeLocalEvent<CarriedComponent, InteractionAttemptEvent>(OnCarriedInteractionAttempt);
        SubscribeLocalEvent<CarriedComponent, SelfBeforeClimbEvent>(OnCarriedBeforeClimb);
        SubscribeLocalEvent<CarriedComponent, UnbuckledEvent>(OnCarriedUnbuckled);
        SubscribeLocalEvent<CarriedComponent, UnbuckleAttemptEvent>(OnCarriedUnbuckleAttempt);
        SubscribeLocalEvent<CarriedComponent, MoveInputEvent>(OnCarriedMoveInput);
        SubscribeLocalEvent<CarriedComponent, CarryEscapeDoAfterEvent>(OnCarryEscapeComplete);

        SubscribeLocalEvent<MobStateComponent, GetVerbsEvent<InteractionVerb>>(OnTargetVerbs);
        SubscribeLocalEvent<CarryModeComponent, ComponentShutdown>(OnCarryModeShutdown);
        SubscribeLocalEvent<CarryModeComponent, BeforeInteractHandEvent>(OnCarrierBeforeInteractHand);
        SubscribeLocalEvent<CarryModeComponent, MobStateChangedEvent>(OnCarryModeMobStateChanged);
        SubscribeLocalEvent<CarryModeComponent, PlayerDetachedEvent>(OnCarryModePlayerDetached);
        SubscribeLocalEvent<CarryModeComponent, MindRemovedMessage>(OnCarryModeMindRemoved);
        SubscribeLocalEvent<CarryAnchorComponent, EntityTerminatingEvent>(OnAnchorTerminating);
        SubscribeLocalEvent<MobStateComponent, CarryDoAfterEvent>(OnCarryDoAfter);
        SubscribeLocalEvent<CarryAttemptTargetComponent, EntityTerminatingEvent>(OnCarryAttemptTargetTerminating);
        SubscribeLocalEvent<CarryAttemptComponent, EntityTerminatingEvent>(OnCarryAttemptCarrierTerminating);
        SubscribeLocalEvent<CarryAttemptComponent, ComponentShutdown>(OnCarryAttemptShutdown);
        SubscribeLocalEvent<CarryAttemptComponent, PlayerDetachedEvent>(OnCarryAttemptPlayerDetached);
        SubscribeLocalEvent<CarryAttemptComponent, MindRemovedMessage>(OnCarryAttemptMindRemoved);
        SubscribeLocalEvent<CarryAttemptComponent, MobStateChangedEvent>(OnCarryAttemptMobStateChanged);
        SubscribeLocalEvent<CarryAttemptTargetComponent, PlayerDetachedEvent>(OnCarryAttemptTargetPlayerDetached);
        SubscribeLocalEvent<CarryAttemptTargetComponent, MindRemovedMessage>(OnCarryAttemptTargetMindRemoved);
        SubscribeLocalEvent<CarryAttemptTargetComponent, MobStateChangedEvent>(OnCarryAttemptTargetMobStateChanged);
        SubscribeLocalEvent<CarryAttemptComponent, StunnedEvent>(OnCarryAttemptStunned);
        SubscribeLocalEvent<CarryAttemptComponent, KnockedDownEvent>(OnCarryAttemptKnockedDownEvent);
    }

    /// <summary>
    /// Validates everything that can be checked before a pickup starts. This
    /// is used both by the verb/key path and again when the do-after completes.
    /// </summary>
    private bool CanStartCarry(EntityUid carrier, EntityUid target, out string? reason)
    {
        reason = null;

        if (carrier == target)
        {
            reason = "carry-cannot-self";
            return false;
        }

        if (TerminatingOrDeleted(carrier) || TerminatingOrDeleted(target) ||
            !TryComp<MobStateComponent>(carrier, out var carrierMob) ||
            !TryComp<MobStateComponent>(target, out var targetMob) ||
            !_mobState.IsAlive(carrier, carrierMob))
        {
            reason = "carry-not-conscious";
            return false;
        }

        if (_mobState.IsDead(target, targetMob))
        {
            reason = "carry-target-dead";
            return false;
        }

        if (!HasComp<HumanoidProfileComponent>(carrier) || !HasComp<HumanoidProfileComponent>(target) ||
            !TryComp<HandsComponent>(carrier, out var hands) ||
            !TryComp<HandsComponent>(target, out _))
        {
            reason = "carry-not-a-person";
            return false;
        }

        if (HasComp<CarryingComponent>(carrier) || HasComp<CarriedComponent>(carrier) ||
            HasComp<CarryAttemptComponent>(carrier))
        {
            reason = HasComp<CarriedComponent>(carrier) ? "carry-carrier-is-carried" : "carry-carrier-busy";
            return false;
        }

        if (HasComp<CarriedComponent>(target) || HasComp<CarryingComponent>(target) ||
            HasComp<CarryAttemptTargetComponent>(target))
        {
            reason = "carry-target-busy";
            return false;
        }

        if (TryComp<BuckleComponent>(carrier, out var carrierBuckle) && carrierBuckle.Buckled)
        {
            reason = "carry-carrier-buckled";
            return false;
        }

        if (TryComp<BuckleComponent>(target, out var targetBuckle) && targetBuckle.Buckled)
        {
            reason = "carry-target-buckled";
            return false;
        }

        if (TryComp<PullerComponent>(carrier, out var carrierPuller) && carrierPuller.Pulling != null)
        {
            reason = "carry-carrier-pulling";
            return false;
        }

        if (TryComp<PullerComponent>(target, out var targetPuller) && targetPuller.Pulling != null)
        {
            reason = "carry-target-pulling";
            return false;
        }

        if (TryComp<PullableComponent>(carrier, out var carrierPullable) && carrierPullable.Puller != null)
        {
            reason = "carry-target-pulled";
            return false;
        }

        if (TryComp<PullableComponent>(target, out var targetPullable) && targetPullable.Puller != null)
        {
            reason = "carry-target-pulled";
            return false;
        }

        if (TryComp<StandingStateComponent>(carrier, out var carrierStanding) && !carrierStanding.Standing)
        {
            reason = "carry-not-conscious";
            return false;
        }

        if (HasComp<StunnedComponent>(carrier) ||
            HasComp<KnockedDownComponent>(carrier) ||
            HasComp<SleepingComponent>(carrier) ||
            HasComp<ChasmFallingComponent>(carrier) ||
            TryComp<CuffableComponent>(carrier, out var carrierCuffs) &&
            !carrierCuffs.CanStillInteract)
        {
            reason = "carry-not-conscious";
            return false;
        }

        if (TryComp<ClimbingComponent>(carrier, out var carrierClimb) && carrierClimb.IsClimbing ||
            TryComp<ClimbingComponent>(target, out var targetClimb) && targetClimb.IsClimbing)
        {
            reason = "carry-not-conscious";
            return false;
        }

        if (_container.IsEntityInContainer(carrier) || _container.IsEntityInContainer(target))
        {
            reason = "carry-not-a-person";
            return false;
        }

        if (CountUsableFreeHands(carrier, hands) < 2)
        {
            reason = "carry-no-free-hands";
            return false;
        }

        if (!_interaction.InRangeUnobstructed(carrier, target, CarryRange, popup: false))
        {
            reason = "carry-too-far";
            return false;
        }

        if (!_blocker.CanInteract(carrier, target))
        {
            reason = "carry-not-conscious";
            return false;
        }

        return true;
    }


    private bool TryStartCarryNow(EntityUid carrier, EntityUid target)
    {
        if (!CanStartCarry(carrier, target, out var reason))
        {
            Popup(carrier, reason!, target);
            return false;
        }

        var anchor = Spawn(CarryAnchor, Transform(carrier).Coordinates);
        var anchorComp = EnsureComp<CarryAnchorComponent>(anchor);
        anchorComp.Carrier = carrier;
        _transform.SetParent(anchor, carrier);
        // The anchor remains world-aligned; the carriage pose itself is
        // controlled by Buckle/RotationVisuals, not by carrier rotation.
        _transform.SetWorldRotation(anchor, Angle.Zero);

        // Start the anchor where the target already is, then slide it into the
        // carrier's hands. Buckling remains authoritative; only the anchor
        // moves, so BuckleTransformCheck never sees an invalid target offset.
        var oldTargetWorld = _transform.GetWorldPosition(target);
        var originalHorizontalRotation = Angle.FromDegrees(90);
        if (TryComp<RotationVisualsComponent>(target, out var targetRotations))
            originalHorizontalRotation = targetRotations.HorizontalRotation;
        var buckleOffset = Comp<StrapComponent>(anchor).BuckleOffset;
        var worldOffset = _transform.GetWorldRotation(anchor).RotateVec(buckleOffset);
        var startAnchorWorld = oldTargetWorld - worldOffset;
        var startAnchorLocal = Vector2.Transform(startAnchorWorld, _transform.GetInvWorldMatrix(carrier));
        _transform.SetLocalPosition(anchor, startAnchorLocal, Transform(anchor));

        if (!_buckle.TryBuckle(target, carrier, anchor, popup: false))
        {
            QueueDel(anchor);
            Popup(carrier, "carry-failed");
            return false;
        }

        var carrying = EnsureComp<CarryingComponent>(carrier);
        var carried = EnsureComp<CarriedComponent>(target);
        carrying.Carried = target;
        carrying.Anchor = anchor;
        carrying.Ending = false;
        carried.Carrier = carrier;
        carried.Anchor = anchor;
        carried.Ending = false;
        carried.OriginalHorizontalRotation = originalHorizontalRotation;
        carrying.AnchorStartLocal = Transform(anchor).LocalPosition;
        carrying.TransitionStart = _timing.CurTime;
        carrying.TransitionEnd = _timing.CurTime + EntryAnimationTime;
        Dirty(carrier, carrying);
        Dirty(target, carried);

        var virtualOk = _virtual.TrySpawnUnremoveableVirtualItemInHand(target, carrier, count: 2);
        if (!virtualOk)
        {
            EndCarry(carrier, target, carrier, "carry-failed");
            return false;
        }

        DisableCarryMode(carrier);
        _audio.PlayPvs(PickupSound, carrier);
        _movement.RefreshMovementSpeedModifiers(carrier);

        Popup(carrier, "carry-started-source", target);
        Popup(target, "carry-started-target", user: carrier);
        return true;
    }

    public bool TryStartCarry(EntityUid carrier, EntityUid target)
    {
        if (!CanStartCarry(carrier, target, out var reason))
        {
            Popup(carrier, reason!, target);
            return false;
        }

        if (HasComp<CarryAttemptComponent>(carrier))
            return false;

        // Start the server-side do-after. EventTarget defaults to the user.
        var attempt = EnsureComp<CarryAttemptComponent>(carrier);
        attempt.Target = target;
        var targetAttempt = EnsureComp<CarryAttemptTargetComponent>(target);
        targetAttempt.Carrier = carrier;
        var args = new DoAfterArgs(EntityManager, carrier, PickupTime, new CarryDoAfterEvent(), carrier, target: target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BlockDuplicate = true,
            CancelDuplicate = true,
        };

        var started = _doAfter.TryStartDoAfter(args, out var doAfterId);
        attempt.DoAfter = doAfterId;
        if (!started)
        {
            ClearAttempt(carrier);
            return false;
        }

        Popup(carrier, "carry-start-source", target);
        Popup(target, "carry-start-target", user: carrier);
        return true;
    }

    /// <summary>
    /// Central idempotent cleanup. All termination paths call this method.
    /// </summary>
    public bool EndCarry(EntityUid carrier, EntityUid? target = null, EntityUid? user = null,
        string? popup = null, bool removeCarrying = true, bool removeCarried = true)
    {
        if (!TryComp<CarryingComponent>(carrier, out var carrying))
            return false;

        if (carrying.Ending)
            return false;

        carrying.Ending = true;
        var carried = target ?? carrying.Carried;
        var anchor = carrying.Anchor;

        // Carrying and a pickup attempt are mutually exclusive, but clear a
        // stray attempt defensively so external state changes cannot leak it.
        if (TryComp<CarryAttemptComponent>(carrier, out _))
            ClearAttempt(carrier);

        if (carried is { } targetUid && TryComp<CarriedComponent>(targetUid, out var carriedComp))
        {
            carriedComp.Ending = true;
            Dirty(targetUid, carriedComp);
        }

        if (carried is { } targetForHands)
            _virtual.DeleteInHandsMatching(carrier, targetForHands);

        if (carried is { } targetToUnbuckle && !TerminatingOrDeleted(targetToUnbuckle) &&
            TryComp<BuckleComponent>(targetToUnbuckle, out var buckle) &&
            anchor is { } anchorUid && buckle.BuckledTo == anchorUid)
        {
            _buckle.Unbuckle(targetToUnbuckle, user ?? carrier);
        }

        // Buckle's Unbuckle resets HorizontalRotation to the prototype default.
        // Restore the captured value afterwards, not before.
        if (carried is { } targetForRotation && !TerminatingOrDeleted(targetForRotation) &&
            TryComp<CarriedComponent>(targetForRotation, out var carriedRotation))
        {
            RestoreHorizontalRotation(targetForRotation, carriedRotation.OriginalHorizontalRotation);
        }

        if (removeCarried && carried is { } targetToRemove && !TerminatingOrDeleted(targetToRemove) &&
            TryComp<CarriedComponent>(targetToRemove, out _))
        {
            RemComp<CarriedComponent>(targetToRemove);
        }

        if (removeCarrying && TryComp<CarryingComponent>(carrier, out _))
            RemComp<CarryingComponent>(carrier);

        if (anchor is { } anchorToDelete && !TerminatingOrDeleted(anchorToDelete))
            QueueDel(anchorToDelete);

        // If the anchor or carrier is being deleted, the buckle cleanup cannot
        // place the target next to a valid parent. Detach it explicitly.
        if (carried is { } targetToDetach && !TerminatingOrDeleted(targetToDetach))
        {
            var xform = Transform(targetToDetach);
            if (xform.ParentUid == anchor || xform.ParentUid == carrier ||
                TerminatingOrDeleted(xform.ParentUid))
                _transform.AttachToGridOrMap(targetToDetach);
        }

        if (carried is { } targetToStand && !TerminatingOrDeleted(targetToStand) &&
            TryComp<MobStateComponent>(targetToStand, out var targetMob) &&
            _mobState.IsAlive(targetToStand, targetMob) &&
            !HasComp<KnockedDownComponent>(targetToStand) &&
            !HasComp<StunnedComponent>(targetToStand) &&
            !HasComp<StunnedStatusEffectComponent>(targetToStand) &&
            !HasComp<SleepingComponent>(targetToStand) &&
            !_mobState.IsIncapacitated(targetToStand))
        {
            _standing.Stand(targetToStand, force: true);
            _blocker.UpdateCanMove(targetToStand);
        }

        if (!TerminatingOrDeleted(carrier))
        {
            _movement.RefreshMovementSpeedModifiers(carrier);
            if (popup != null)
                Popup(carrier, popup, carried is { } popupTarget && !TerminatingOrDeleted(popupTarget) ? popupTarget : null);
        }

        return true;
    }

    public bool TryEndCarry(EntityUid carrier, EntityUid? user = null)
    {
        if (!TryComp<CarryingComponent>(carrier, out var carrying) || carrying.Ending)
            return false;

        var target = carrying.Carried;
        if (!EndCarry(carrier, target, user))
            return false;

        if (target is { } targetUid && !TerminatingOrDeleted(targetUid))
        {
            if (user == targetUid)
            {
                Popup(targetUid, "carry-escape-success", user: carrier);
            }
            else
            {
                Popup(carrier, "carry-dropped-source", targetUid);
                Popup(targetUid, "carry-dropped-target", user: carrier);
            }
        }
        return true;
    }

    private void DisableCarryMode(EntityUid carrier)
    {
        if (!TryComp<CarryModeComponent>(carrier, out var mode) || !mode.Enabled)
            return;

        mode.Enabled = false;
        Dirty(carrier, mode);
        _alerts.ClearAlert(carrier, mode.Alert);
    }

    private int CountUsableFreeHands(EntityUid carrier, HandsComponent hands)
    {
        // Cuffable inserts unremoveable virtual cuff items into free hands, so
        // CountFreeHands already reflects them. Subtracting CuffedHandCount
        // again would double-count the occupied hands.
        return _hands.CountFreeHands((carrier, hands));
    }

    private void RestoreHorizontalRotation(EntityUid target, Angle angle)
    {
        if (!TryComp<RotationVisualsComponent>(target, out var rotationVisuals))
            return;

        rotationVisuals.HorizontalRotation = angle;
        Dirty(target, rotationVisuals);
    }

    private void Popup(EntityUid recipient, string key, EntityUid? target = null, EntityUid? user = null)
    {
        var message = Loc.GetString(key,
            ("target", target is { } targetUid ? Identity.Entity(targetUid, EntityManager) : recipient),
            ("user", user is { } userUid ? Identity.Entity(userUid, EntityManager) : recipient));
        _popup.PopupEntity(message, recipient, recipient);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CarryingComponent>();
        while (query.MoveNext(out var carrier, out var carrying))
        {
            if (carrying.Ending)
                continue;

            if (carrying.Carried is not { } target || carrying.Anchor is not { } anchor ||
                TerminatingOrDeleted(target) || TerminatingOrDeleted(anchor) ||
                TerminatingOrDeleted(carrier))
            {
                EndCarry(carrier, carrying.Carried, carrier, "carry-dropped-dead");
                continue;
            }

            if (carrying.TransitionEnd > _timing.CurTime)
            {
                var duration = (carrying.TransitionEnd - carrying.TransitionStart).TotalSeconds;
                var progress = duration <= 0
                    ? 1f
                    : (float) ((_timing.CurTime - carrying.TransitionStart).TotalSeconds / duration);
                progress = Math.Clamp(progress, 0f, 1f);
                var local = Vector2.Lerp(carrying.AnchorStartLocal, Vector2.Zero, progress);
                _transform.SetLocalPosition(anchor, local, Transform(anchor));
            }
            else if (Transform(anchor).LocalPosition != Vector2.Zero)
            {
                _transform.SetLocalPosition(anchor, Vector2.Zero, Transform(anchor));
            }

            if (Transform(carrier).MapID != Transform(target).MapID)
            {
                EndCarry(carrier, target, carrier, "carry-dropped-dead");
                continue;
            }

            if (HasComp<CarriedComponent>(carrier) || HasComp<CarryingComponent>(target) ||
                !HasComp<HumanoidProfileComponent>(carrier) || !HasComp<HumanoidProfileComponent>(target) ||
                !TryComp<HandsComponent>(carrier, out var hands) ||
                !TryComp<HandsComponent>(target, out _) ||
                !TryComp<MobStateComponent>(carrier, out var carrierMob) ||
                !_mobState.IsAlive(carrier, carrierMob) ||
                !TryComp<MobStateComponent>(target, out var targetMob) ||
                _mobState.IsDead(target, targetMob) ||
                !TryComp<BuckleComponent>(target, out var buckle) ||
                buckle.BuckledTo != anchor ||
                Transform(anchor).ParentUid != carrier)
            {
                EndCarry(carrier, target, carrier, "carry-dropped-dead");
            }
        }
    }

    private void OnCarryingInit(Entity<CarryingComponent> ent, ref ComponentInit args)
    {
        _alerts.ShowAlert(ent.Owner, ent.Comp.Alert);
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnCarryingShutdown(Entity<CarryingComponent> ent, ref ComponentShutdown args)
    {
        if (!ent.Comp.Ending)
        {
            // Component shutdown is still inside the component lifecycle, so
            // the normal central cleanup can resolve and release everything.
            EndCarry(ent.Owner, ent.Comp.Carried, ent.Owner, null, removeCarrying: false);
        }

        if (!TerminatingOrDeleted(ent.Owner))
        {
            _alerts.ClearAlert(ent.Owner, ent.Comp.Alert);
            _movement.RefreshMovementSpeedModifiers(ent.Owner);
        }
    }

    private void OnCarryDropAlert(Entity<CarryingComponent> carrier, ref CarryDropAlertEvent args)
    {
        if (args.Handled || args.User != carrier.Owner)
            return;

        args.Handled = true;
        TryEndCarry(carrier.Owner, carrier.Owner);
    }

    private void OnCarryDropRequest(CarryDropRequestEvent args, EntitySessionEventArgs eventArgs)
    {
        if (eventArgs.SenderSession.AttachedEntity is not { } carrier)
            return;

        if (HasComp<CarryingComponent>(carrier))
            TryEndCarry(carrier, carrier);
    }

    private void OnCarrierRefreshSpeed(Entity<CarryingComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.WalkSpeedModifier, ent.Comp.SprintSpeedModifier);
    }
}
