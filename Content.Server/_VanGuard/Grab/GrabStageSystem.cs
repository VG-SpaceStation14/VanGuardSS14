using Content.Shared.Actions.Events;
using Content.Shared.Alert;
using Content.Server.DoAfter;
using System.Numerics;
using Content.Shared._VanGuard.Grab;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Speech;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Grab;

/// <summary>
/// Adds explicit soft/hard/choke grab stages on top of the current pull system.
/// Pulling remains the source of truth for the actual joint and target ownership.
/// </summary>
public sealed partial class GrabStageSystem : EntitySystem
{
    private const float MaxGrabDistance = 2.1f;
    private static readonly TimeSpan StageCooldown = TimeSpan.FromSeconds(0.5);
    private static readonly SoundPathSpecifier GrabSound = new("/Audio/Effects/thudswoosh.ogg");

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private AlertsSystem _alerts = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PullerComponent, PullStartedMessage>(OnPullStarted);
        SubscribeLocalEvent<PullerComponent, PullStoppedMessage>(OnPullStopped);
        SubscribeLocalEvent<PullerComponent, GrabStageToggleEvent>(OnGrabToggle);
        SubscribeLocalEvent<GrabStageTargetComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<GrabStageTargetComponent, DisarmAttemptEvent>(OnGrabbedDisarmAttempt);
        SubscribeLocalEvent<PullerComponent, GrabThrowRequestEvent>(OnThrowRequest);
        SubscribeLocalEvent<PullableComponent, GrabEscapeDoAfterEvent>(OnEscapeComplete);
        SubscribeLocalEvent<PullableComponent, SpeakAttemptEvent>(OnSpeakAttempt);
        SubscribeLocalEvent<PullableComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
        SubscribeLocalEvent<GrabStageComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovespeed);
    }

    private void OnPullStarted(Entity<PullerComponent> puller, ref PullStartedMessage args)
    {
        if (args.PullerUid != puller.Owner || !HasComp<MobStateComponent>(args.PulledUid))
            return;

        var comp = EnsureComp<GrabStageComponent>(puller.Owner);
        comp.Stage = GrabStage.None;
        comp.Grabbing = args.PulledUid;
        comp.NextStageChange = _timing.CurTime;
        EnsureComp<GrabStageTargetComponent>(args.PulledUid);
        Dirty(puller.Owner, comp);
    }

    private void OnPullStopped(Entity<PullerComponent> puller, ref PullStoppedMessage args)
    {
        if (!TryComp<GrabStageComponent>(puller.Owner, out var comp))
            return;

        if (comp.Grabbing is { } target)
            RemComp<GrabStageTargetComponent>(target);
        RemComp<GrabStageComponent>(puller.Owner);
    }

    private void OnGrabbedDisarmAttempt(Entity<GrabStageTargetComponent> target, ref DisarmAttemptEvent args)
    {
        if (args.DisarmerUid != target.Owner ||
            !TryComp<PullerComponent>(args.TargetUid, out var puller) ||
            puller.Pulling != target.Owner ||
            !TryComp<GrabStageComponent>(args.TargetUid, out var stage) ||
            stage.Stage < GrabStage.Soft)
            return;

        args.Cancelled = true;
    }

    private void OnGrabToggle(Entity<PullerComponent> puller, ref GrabStageToggleEvent args)
    {
        if (args.Puller != puller.Owner)
            return;

        // A normal pull has no GrabStageComponent and falls through to stopping.
        if (!TryComp<GrabStageComponent>(puller.Owner, out _))
            return;

        args.Handled = true;
        TryAdvanceStage(puller.Owner);
    }

    public bool TryAdvanceStage(EntityUid puller)
    {
        if (!TryComp<GrabStageComponent>(puller, out var comp) ||
            !TryComp<PullerComponent>(puller, out var pullerComp) ||
            pullerComp.Pulling is not { } target)
            return false;

        if (comp.Stage >= GrabStage.Choke || _timing.CurTime < comp.NextStageChange)
            return false;

        if (!ValidateGrab(puller, target, comp))
            return false;

        var newStage = comp.Stage + 1;

        if (newStage == GrabStage.Choke &&
            (!TryComp<HandsComponent>(puller, out var hands) ||
             _hands.CountFreeableHands((puller, hands)) < 1))
        {
            _popup.PopupEntity(Loc.GetString("grab-stage-no-hands"), puller, puller);
            return false;
        }

        var oldStage = comp.Stage;
        comp.Stage = newStage;
        comp.NextStageChange = _timing.CurTime + StageCooldown;
        Dirty(puller, comp);

        var changed = new GrabStageChangedEvent(puller, target, oldStage, newStage);
        RaiseLocalEvent(puller, ref changed);
        RaiseLocalEvent(target, ref changed);
        ShowStageAlerts(puller, target, newStage);

        // Old grab behavior: a chokehold forces the victim to drop held items.
        if (newStage == GrabStage.Choke && TryComp<HandsComponent>(target, out var targetHands))
        {
            foreach (var hand in targetHands.Hands.Keys)
                _hands.TryDrop((target, targetHands), hand);
        }

        var popup = Loc.GetString("grab-stage-advance",
            ("user", Identity.Entity(puller, EntityManager)),
            ("stage", Loc.GetString($"grab-stage-{newStage.ToString().ToLowerInvariant()}")),
            ("target", Identity.Entity(target, EntityManager)));
        _popup.PopupEntity(popup, puller, puller, PopupType.SmallCaution);
        _audio.PlayPvs(GrabSound, puller);
        return true;
    }

    public bool TryLowerStage(EntityUid puller, EntityUid? user = null)
    {
        if (!TryComp<GrabStageComponent>(puller, out var comp) ||
            !TryComp<PullerComponent>(puller, out var pullerComp) ||
            pullerComp.Pulling is not { } target)
            return false;

        if (_timing.CurTime < comp.NextStageChange && user != target)
            return false;

        if (comp.Stage == GrabStage.None)
        {
            StopGrab(puller, target, comp);
            return true;
        }

        var oldStage = comp.Stage;
        comp.Stage--;
        comp.NextStageChange = _timing.CurTime + StageCooldown;

        if (comp.Stage == GrabStage.None)
        {
            StopGrab(puller, target, comp);
            return true;
        }

        Dirty(puller, comp);
        var changed = new GrabStageChangedEvent(puller, target, oldStage, comp.Stage);
        RaiseLocalEvent(puller, ref changed);
        RaiseLocalEvent(target, ref changed);
        ShowStageAlerts(puller, target, comp.Stage);

        if (user != target)
        {
            _popup.PopupEntity(Loc.GetString("grab-stage-lower",
                ("user", Identity.Entity(puller, EntityManager)),
                ("stage", Loc.GetString($"grab-stage-{comp.Stage.ToString().ToLowerInvariant()}")),
                ("target", Identity.Entity(target, EntityManager))), puller, puller);
            _audio.PlayPvs(GrabSound, puller);
        }

        return true;
    }

    public bool TryStartEscape(EntityUid puller, EntityUid target)
    {
        if (!TryComp<GrabStageComponent>(puller, out var comp) ||
            !TryComp<PullerComponent>(puller, out var pullerComp) ||
            pullerComp.Pulling != target ||
            comp.Stage == GrabStage.None)
            return false;

        var args = new DoAfterArgs(EntityManager, target, TimeSpan.FromSeconds(1 + (int) comp.Stage * 0.5f),
            new GrabEscapeDoAfterEvent(), target, target: target)
        {
            BreakOnDamage = false,
            BreakOnMove = false,
            NeedHand = false,
            BlockDuplicate = true,
            CancelDuplicate = true,
        };

        return _doAfter.TryStartDoAfter(args);
    }

    private void OnEscapeComplete(Entity<PullableComponent> pullable, ref GrabEscapeDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || pullable.Comp.Puller is not { } puller)
            return;

        args.Handled = true;
        TryLowerStage(puller, pullable.Owner);
        _popup.PopupEntity(Loc.GetString("grab-stage-escape"), pullable.Owner, pullable.Owner);
    }

    private void OnSpeakAttempt(Entity<PullableComponent> pullable, ref SpeakAttemptEvent args)
    {
        if (pullable.Comp.Puller is not { } puller ||
            !TryComp<GrabStageComponent>(puller, out var stage) ||
            stage.Stage < GrabStage.Choke)
            return;

        _popup.PopupEntity(Loc.GetString("grab-stage-choke-speech"), pullable.Owner, pullable.Owner);
        args.Cancel();
    }

    private void OnUpdateCanMove(Entity<PullableComponent> pullable, ref UpdateCanMoveEvent args)
    {
        if (pullable.Comp.Puller is not { } puller ||
            !TryComp<GrabStageComponent>(puller, out var stage) ||
            stage.Stage == GrabStage.None)
            return;

        args.Cancel();
    }

    private void OnRefreshMovespeed(Entity<GrabStageComponent> stage, ref RefreshMovementSpeedModifiersEvent args)
    {
        var modifier = stage.Comp.Stage switch
        {
            GrabStage.Soft => 0.95f,
            GrabStage.Hard => 0.9f,
            GrabStage.Choke => 0.85f,
            _ => 1f,
        };

        args.ModifySpeed(modifier, modifier);
    }

    private void OnGetVerbs(Entity<GrabStageTargetComponent> target, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !TryComp<PullableComponent>(target, out var pullable) ||
            pullable.Puller is not { } puller || !TryComp<GrabStageComponent>(puller, out var stage))
            return;

        if (args.User == puller)
        {
            if (stage.Stage < GrabStage.Choke)
            {
                args.Verbs.Add(new Verb
                {
                    Text = Loc.GetString("grab-stage-verb-tighten"),
                    Act = () => TryAdvanceStage(puller),
                    DoContactInteraction = false,
                });
            }

            if (stage.Stage > GrabStage.None)
            {
                args.Verbs.Add(new Verb
                {
                    Text = Loc.GetString("grab-stage-verb-loosen"),
                    Act = () => TryLowerStage(puller),
                    DoContactInteraction = false,
                });
            }

            if (stage.Stage >= GrabStage.Hard)
            {
                args.Verbs.Add(new Verb
                {
                    Text = Loc.GetString("grab-stage-verb-throw"),
                    Act = () => TryThrow(puller, target.Owner),
                    DoContactInteraction = false,
                });
            }
        }
        else if (args.User == target.Owner && stage.Stage > GrabStage.None)
        {
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("grab-stage-verb-escape"),
                Act = () => TryStartEscape(puller, target.Owner),
                DoContactInteraction = false,
            });
        }
    }

    private void OnThrowRequest(Entity<PullerComponent> puller, ref GrabThrowRequestEvent args)
    {
        if (!TryComp<GrabStageComponent>(puller.Owner, out var stage) ||
            stage.Grabbing != args.Target ||
            puller.Comp.Pulling != args.Target)
            return;

        args.Handled = true;

        if (stage.Stage < GrabStage.Hard)
        {
            _popup.PopupEntity(Loc.GetString("grab-stage-need-hard"), puller, puller);
            return;
        }

        TryThrow(puller.Owner, args.Target, args.Coordinates);
    }

    public bool TryThrow(EntityUid puller, EntityUid target, EntityCoordinates? coordinates = null)
    {
        if (!TryComp<GrabStageComponent>(puller, out var stage) || stage.Stage < GrabStage.Hard)
            return false;

        var direction = coordinates is { } coords
            ? _transform.ToMapCoordinates(coords).Position - Transform(puller).WorldPosition
            : Transform(target).WorldPosition - Transform(puller).WorldPosition;

        if (direction.LengthSquared() < 0.001f)
            return false;

        StopGrab(puller, target, stage);
        var thrown = EnsureComp<GrabThrownComponent>(target);
        thrown.Thrower = puller;
        _throwing.TryThrow(target, direction, 8f, puller);
        _audio.PlayPvs(GrabSound, target);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GrabStageComponent>();
        while (query.MoveNext(out var puller, out var comp))
        {
            if (_timing.CurTime < comp.NextDistanceCheck)
                continue;

            comp.NextDistanceCheck = _timing.CurTime + TimeSpan.FromSeconds(0.25);

            if (!TryComp<PullerComponent>(puller, out var pullerComp) ||
                pullerComp.Pulling is not { } target ||
                !TryComp<PullableComponent>(target, out var pullable) ||
                pullable.Puller != puller)
            {
                if (comp.Grabbing is { } oldTarget)
                    RemComp<GrabStageTargetComponent>(oldTarget);
                RemComp<GrabStageComponent>(puller);
                continue;
            }

            if (!ValidateGrab(puller, target, comp))
                StopGrab(puller, target, comp);
        }
    }

    private void ShowStageAlerts(EntityUid puller, EntityUid target, GrabStage stage)
    {
        if (TryComp<PullerComponent>(puller, out var pullerComp))
            _alerts.ShowAlert(puller, pullerComp.PullingAlert, (short) stage);

        if (TryComp<PullableComponent>(target, out var pullableComp))
            _alerts.ShowAlert(target, pullableComp.PulledAlert, (short) stage);
    }

    private bool ValidateGrab(EntityUid puller, EntityUid target, GrabStageComponent comp)
    {
        if (TerminatingOrDeleted(puller) || TerminatingOrDeleted(target) ||
            _mobState.IsDead(puller) || _mobState.IsDead(target))
            return false;

        if (!_interaction.InRangeUnobstructed(puller, target, MaxGrabDistance))
            return false;

        return true;
    }

    private void StopGrab(EntityUid puller, EntityUid target, GrabStageComponent comp)
    {
        if (TryComp<PullableComponent>(target, out var pullable))
            _pulling.TryStopPull(target, pullable, puller);

        RemComp<GrabStageTargetComponent>(target);
        if (TryComp<GrabStageComponent>(puller, out _))
            RemComp<GrabStageComponent>(puller);
    }
}
