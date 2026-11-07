using System.Linq;
using Content.Server.DoAfter;
using Content.Shared._VanGuard.Grab;
using Content.Shared._VanGuard.MartialArts;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.CombatMode;
using Content.Shared.Interaction.Events;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Standing;
using Content.Shared.StatusEffectNew;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.MartialArts;

/// <summary>
/// Server-authoritative martial arts progression, combo tracking, active moves, books and clothing.
/// </summary>
public sealed partial class MartialArtsSystem : EntitySystem
{
    private const int MaxHistory = 5;
    private static readonly TimeSpan ComboWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PreparedLifetime = TimeSpan.FromSeconds(6);
    private static readonly EntProtoId CycleActionId = "ActionMartialCycle";
    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private BlindableSystem _blindable = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MartialArtsKnowledgeComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<MartialArtsKnowledgeComponent, DisarmAttemptEvent>(OnDisarmAttempt);
        SubscribeLocalEvent<MartialArtsKnowledgeComponent, GrabStageChangedEvent>(OnGrabStageChanged);
        SubscribeLocalEvent<MartialArtsKnowledgeComponent, ToggleCombatActionEvent>(OnCombatToggled);
        SubscribeLocalEvent<MartialArtsKnowledgeComponent, MartialArtActionEvent>(OnMartialArtAction);
        SubscribeLocalEvent<MartialArtsKnowledgeComponent, MartialArtCycleEvent>(OnCycleStyle);
        SubscribeLocalEvent<MartialArtsKnowledgeComponent, ComponentStartup>(OnKnowledgeStartup);
        SubscribeLocalEvent<MartialArtsKnowledgeComponent, ComponentShutdown>(OnKnowledgeShutdown);

        SubscribeLocalEvent<MartialArtsBookComponent, UseInHandEvent>(OnBookUse);
        SubscribeLocalEvent<MartialArtsBookComponent, MartialArtsBookDoAfterEvent>(OnBookLearned);

        SubscribeLocalEvent<MartialArtsClothingComponent, GotEquippedEvent>(OnClothingEquipped);
        SubscribeLocalEvent<MartialArtsClothingComponent, GotUnequippedEvent>(OnClothingUnequipped);

        SubscribeLocalEvent<MartialArtsNoShootComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    #region Learning and style selection

    public bool TryLearnStyle(EntityUid user, ProtoId<MartialArtPrototype> style, bool permanent)
    {
        if (!_prototypes.TryIndex(style, out var prototype))
            return false;

        var comp = EnsureComp<MartialArtsKnowledgeComponent>(user);

        if (permanent)
        {
            if (comp.KnownStyles.Contains(style))
                return false;

            comp.KnownStyles.Add(style);
        }
        else
        {
            if (comp.TemporaryStyles.Contains(style) || comp.KnownStyles.Contains(style))
                return false;

            comp.TemporaryStyles.Add(style);
        }

        if (comp.CurrentStyle == null)
            SetCurrentStyle(user, comp, style);
        else
            RefreshActions(user, comp);

        return true;
    }

    public bool HasStyle(Entity<MartialArtsKnowledgeComponent?> ent, ProtoId<MartialArtPrototype> style)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        return ent.Comp.KnownStyles.Contains(style) || ent.Comp.TemporaryStyles.Contains(style);
    }

    public bool IsCurrentStyle(Entity<MartialArtsKnowledgeComponent?> ent, ProtoId<MartialArtPrototype> style)
    {
        return Resolve(ent, ref ent.Comp, false) && ent.Comp.CurrentStyle == style;
    }

    private void SetCurrentStyle(EntityUid user, MartialArtsKnowledgeComponent comp, ProtoId<MartialArtPrototype>? style)
    {
        comp.CurrentStyle = style;
        comp.PreparedMove = null;
        comp.History.Clear();
        comp.Counter = 0;
        comp.CounterExpires = TimeSpan.Zero;
        RemoveGrantedActions(comp);
        RemoveReflect(user);
        RemComp<MartialArtsNoShootComponent>(user);
        RefreshActions(user, comp);

        if (style == null)
        {
            RestoreMeleeVisuals(user, comp);
            return;
        }

        ApplyMeleeVisuals(user, comp);

        if (_prototypes.TryIndex(style, out var prototype) && prototype.ReflectProbability > 0f)
        {
            var reflect = EnsureComp<ReflectComponent>(user);
            reflect.ReflectProb = prototype.ReflectProbability;
            reflect.Reflects = ReflectType.Energy | ReflectType.NonEnergy;
            reflect.InRightPlace = true;
            reflect.ReflectingInHands = true;
            Dirty(user, reflect);
        }

        if (_prototypes.TryIndex(style, out var activeStyle) && !activeStyle.CanShoot)
            EnsureComp<MartialArtsNoShootComponent>(user);
    }

    private void RemoveReflect(EntityUid user)
    {
        if (TryComp<ReflectComponent>(user, out var reflect))
            RemComp<ReflectComponent>(user);
    }

    private void ForgetStyle(EntityUid user, MartialArtsKnowledgeComponent comp, ProtoId<MartialArtPrototype> style)
    {
        if (comp.KnownStyles.Contains(style))
            return;

        comp.TemporaryStyles.Remove(style);
        if (comp.CurrentStyle != style)
            return;

        ProtoId<MartialArtPrototype>? next = null;
        if (comp.KnownStyles.Count > 0)
            next = comp.KnownStyles.First();
        else if (comp.TemporaryStyles.Count > 0)
            next = comp.TemporaryStyles.First();

        SetCurrentStyle(user, comp, next);
    }


    private void RefreshActions(EntityUid user, MartialArtsKnowledgeComponent comp)
    {
        RemoveGrantedActions(comp);

        if (comp.CurrentStyle is not { } current ||
            !_prototypes.TryIndex(current, out var style))
        {
            RemoveCycleAction(user, comp);
            return;
        }

        foreach (var moveId in style.Moves)
        {
            if (!_prototypes.TryIndex(moveId, out var move) || move.Action is not { } actionId)
                continue;

            var action = _actions.AddAction(user, actionId.Id);
            if (action != null)
                comp.GrantedActions[moveId] = action.Value;
        }

        var styleCount = comp.KnownStyles.Count + comp.TemporaryStyles.Count;
        if (styleCount > 1)
        {
            if (comp.CycleAction == null)
            {
                comp.CycleAction = _actions.AddAction(user, CycleActionId.Id);
            }
        }
        else
        {
            RemoveCycleAction(user, comp);
        }
    }

    private void RemoveGrantedActions(MartialArtsKnowledgeComponent comp)
    {
        foreach (var action in comp.GrantedActions.Values)
        {
            if (!TerminatingOrDeleted(action))
                _actions.RemoveAction(action);
        }

        comp.GrantedActions.Clear();
    }

    private void RemoveCycleAction(EntityUid user, MartialArtsKnowledgeComponent comp)
    {
        if (comp.CycleAction is { } action && !TerminatingOrDeleted(action))
            _actions.RemoveAction(action);

        comp.CycleAction = null;
    }

    private void OnKnowledgeStartup(Entity<MartialArtsKnowledgeComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.CurrentStyle == null)
        {
            ProtoId<MartialArtPrototype>? next = null;
            if (ent.Comp.KnownStyles.Count > 0)
                next = ent.Comp.KnownStyles.First();
            else if (ent.Comp.TemporaryStyles.Count > 0)
                next = ent.Comp.TemporaryStyles.First();

            if (next is { } selected)
                SetCurrentStyle(ent.Owner, ent.Comp, selected);
        }
        else
        {
            RefreshActions(ent.Owner, ent.Comp);
        }
    }

    private void OnCycleStyle(Entity<MartialArtsKnowledgeComponent> ent, ref MartialArtCycleEvent args)
    {
        var styles = ent.Comp.KnownStyles
            .Concat(ent.Comp.TemporaryStyles)
            .Distinct()
            .ToList();

        if (styles.Count <= 1)
        {
            _popup.PopupEntity(Loc.GetString("vg-martial-no-other-style"), ent.Owner, ent.Owner);
            args.Handled = true;
            return;
        }

        var current = ent.Comp.CurrentStyle;
        var index = current is { } id ? styles.IndexOf(id) : -1;
        var next = styles[(index + 1) % styles.Count];
        SetCurrentStyle(ent.Owner, ent.Comp, next);

        if (_prototypes.TryIndex(next, out var style))
            _popup.PopupEntity(Loc.GetString("vg-martial-style-selected", ("style", Loc.GetString(style.Name))), ent.Owner, ent.Owner);

        args.Handled = true;
    }

    #endregion

    private void ApplyMeleeVisuals(EntityUid user, MartialArtsKnowledgeComponent comp)
    {
        if (!TryComp<MeleeWeaponComponent>(user, out var melee))
            return;

        comp.OriginalAnimation ??= melee.Animation;
        comp.OriginalWideAnimation ??= melee.WideAnimation;
        melee.Animation = "WeaponArcMartialPrimary";
        melee.WideAnimation = "WeaponArcMartialSecondary";
        Dirty(user, melee);
    }

    private void RestoreMeleeVisuals(EntityUid user, MartialArtsKnowledgeComponent comp)
    {
        if (comp.OriginalAnimation == null && comp.OriginalWideAnimation == null)
            return;

        if (TryComp<MeleeWeaponComponent>(user, out var melee))
        {
            if (comp.OriginalAnimation is { } animation)
                melee.Animation = animation;
            if (comp.OriginalWideAnimation is { } wideAnimation)
                melee.WideAnimation = wideAnimation;
            Dirty(user, melee);
        }

        comp.OriginalAnimation = null;
        comp.OriginalWideAnimation = null;
    }

    #region Combo tracking

    private void OnMeleeHit(Entity<MartialArtsKnowledgeComponent> ent, ref MeleeHitEvent args)
    {
        if (args.User != ent.Owner || !args.IsHit || args.HitEntities.Count == 0)
            return;

        // The event is raised on the melee weapon. Unarmed attacks raise it on the body itself.
        if (args.Weapon != ent.Owner)
            return;

        var target = args.HitEntities.First();
        if (!IsValidTarget(ent.Owner, target))
            return;

        if (ent.Comp.CurrentStyle is not { } current ||
            !_prototypes.TryIndex(current, out var style))
            return;

        TryNeckSnap(ent.Owner, target, style, ent.Comp);
        if (_mobState.IsDead(target))
            return;

        // A prepared active move consumes the melee hit instead of feeding a passive combo.
        if (ent.Comp.PreparedMove is { } prepared)
        {
            if (_timing.CurTime > ent.Comp.PreparedUntil)
            {
                ent.Comp.PreparedMove = null;
            }
            else if (_prototypes.TryIndex(prepared, out var preparedMove))
            {
                ent.Comp.PreparedMove = null;
                ExecuteMove(ent.Owner, target, preparedMove, style, ent.Comp);
                return;
            }
        }

        if (style.UnarmedBonusDamage != null)
            args.BonusDamage += style.UnarmedBonusDamage;

        RecordAction(ent.Owner, target, MartialArtActionType.Hit, ent.Comp, style);
    }

    private void OnDisarmAttempt(Entity<MartialArtsKnowledgeComponent> ent, ref DisarmAttemptEvent args)
    {
        if (args.DisarmerUid != ent.Owner)
            return;

        var comp = ent.Comp;

        if (args.DisarmerUid == args.TargetUid || !IsValidTarget(args.DisarmerUid, args.TargetUid))
            return;

        if (comp.CurrentStyle is not { } current ||
            !_prototypes.TryIndex(current, out var style))
            return;

        RecordAction(args.DisarmerUid, args.TargetUid, MartialArtActionType.Disarm, comp, style);
    }

    private void OnGrabStageChanged(Entity<MartialArtsKnowledgeComponent> ent, ref GrabStageChangedEvent args)
    {
        if (args.Puller != ent.Owner || args.NewStage <= args.OldStage)
            return;

        if (!IsValidTarget(args.Puller, args.Pulled))
            return;

        if (ent.Comp.CurrentStyle is not { } current ||
            !_prototypes.TryIndex(current, out var style))
            return;

        RecordAction(args.Puller, args.Pulled, MartialArtActionType.Grab, ent.Comp, style);
    }

    private void OnKnowledgeShutdown(Entity<MartialArtsKnowledgeComponent> ent, ref ComponentShutdown args)
    {
        RestoreMeleeVisuals(ent.Owner, ent.Comp);
    }

    private void OnCombatToggled(Entity<MartialArtsKnowledgeComponent> ent, ref ToggleCombatActionEvent args)
    {
        ent.Comp.History.Clear();
        ent.Comp.PreparedMove = null;
    }

    private void RecordAction(
        EntityUid user,
        EntityUid target,
        MartialArtActionType action,
        MartialArtsKnowledgeComponent comp,
        MartialArtPrototype style)
    {
        var now = _timing.CurTime;
        comp.History.RemoveAll(entry => now - entry.At > ComboWindow || entry.Target != target);

        if (comp.History.Count > 0 && comp.History[^1].Target != target)
            comp.History.Clear();

        comp.History.Add(new MartialArtsComboEntry(action, target, now));
        while (comp.History.Count > MaxHistory)
            comp.History.RemoveAt(0);

        if (now < comp.NextComboAt)
            return;

        MartialArtMovePrototype? match = null;
        var matchLength = 0;

        foreach (var moveId in style.Moves)
        {
            if (!_prototypes.TryIndex(moveId, out var move) || move.Sequence.Count == 0)
                continue;

            if (move.Sequence.Count <= matchLength || move.Sequence.Count > comp.History.Count)
                continue;

            var offset = comp.History.Count - move.Sequence.Count;
            var matched = true;
            for (var i = 0; i < move.Sequence.Count; i++)
            {
                if (comp.History[offset + i].Action != move.Sequence[i])
                {
                    matched = false;
                    break;
                }
            }

            if (!matched)
                continue;

            match = move;
            matchLength = move.Sequence.Count;
        }

        if (match == null)
            return;

        if (!CanExecuteMove(user, target, match, out var failure))
        {
            comp.History.Clear();
            _popup.PopupEntity(Loc.GetString(failure), user, user);
            return;
        }

        ExecuteMove(user, target, match, style, comp);
        comp.History.Clear();
        comp.NextComboAt = now + TimeSpan.FromSeconds(style.ComboCooldown);
    }

    #endregion

    #region Active moves

    private void OnMartialArtAction(Entity<MartialArtsKnowledgeComponent> ent, ref MartialArtActionEvent args)
    {
        args.Handled = true;

        if (ent.Comp.CurrentStyle is not { } current ||
            !_prototypes.TryIndex(current, out var style) ||
            !_prototypes.TryIndex(args.Move, out var move) ||
            !style.Moves.Contains(args.Move))
        {
            _popup.PopupEntity(Loc.GetString("vg-martial-move-not-known"), ent.Owner, ent.Owner);
            return;
        }

        if (_mobState.IsDead(ent.Owner))
            return;

        if (move.Action == null || move.Sequence.Count > 0)
        {
            _popup.PopupEntity(Loc.GetString("vg-martial-move-passive"), ent.Owner, ent.Owner);
            return;
        }

        if (ent.Comp.MoveCooldowns.TryGetValue(args.Move, out var next) && _timing.CurTime < next)
            return;

        ent.Comp.PreparedMove = args.Move;
        ent.Comp.PreparedUntil = _timing.CurTime + PreparedLifetime;
        _popup.PopupEntity(Loc.GetString("vg-martial-move-ready", ("move", Loc.GetString(move.Name))), ent.Owner, ent.Owner);
    }

    private bool CanExecuteMove(EntityUid user, EntityUid target, MartialArtMovePrototype move, out string failure)
    {
        failure = "vg-martial-failed";

        if (TerminatingOrDeleted(user) || TerminatingOrDeleted(target) || target == user)
            return false;

        if (_mobState.IsDead(target))
            return false;

        if (!_interaction.InRangeUnobstructed(user, target, move.Range))
        {
            failure = "vg-martial-failed-range";
            return false;
        }

        var downed = _standing.IsDown(target);
        if (move.RequiresStandingTarget && downed)
        {
            failure = "vg-martial-failed-standing";
            return false;
        }

        if (move.RequiresDownedTarget && !downed)
        {
            failure = "vg-martial-failed-downed";
            return false;
        }

        if (move.RequiresTargetPullingUser)
        {
            if (!TryComp<PullerComponent>(target, out var targetPuller) || targetPuller.Pulling != user ||
                !TryComp<GrabStageComponent>(target, out var targetGrab) ||
                targetGrab.Stage < GrabStage.Soft || targetGrab.Grabbing != user)
            {
                failure = "vg-martial-failed-grab";
                return false;
            }
        }

        if (move.RequiresUserPullingTarget)
        {
            if (!TryComp<PullerComponent>(user, out var userPuller) || userPuller.Pulling != target ||
                !TryComp<GrabStageComponent>(user, out var userGrab) ||
                userGrab.Stage < GrabStage.Soft || userGrab.Grabbing != target)
            {
                failure = "vg-martial-failed-grab";
                return false;
            }
        }

        return true;
    }

    private void ExecuteMove(
        EntityUid user,
        EntityUid target,
        MartialArtMovePrototype move,
        MartialArtPrototype style,
        MartialArtsKnowledgeComponent comp)
    {
        if (!CanExecuteMove(user, target, move, out var failure))
        {
            _popup.PopupEntity(Loc.GetString(failure), user, user);
            return;
        }

        if (move.Cooldown > 0)
            comp.MoveCooldowns[move.ID] = _timing.CurTime + TimeSpan.FromSeconds(move.Cooldown);

        foreach (var effect in move.Effects)
            ApplyEffect(user, target, effect, style, comp);

        if (move.StopPull && TryComp<PullerComponent>(user, out var puller) && puller.Pulling == target &&
            TryComp<PullableComponent>(target, out var pullable))
        {
            _pulling.TryStopPull(target, pullable, user);
        }

        TryNeckSnap(user, target, style, comp);
    }

    public void ApplyEffect(
        EntityUid user,
        EntityUid target,
        IMartialArtEffect effect,
        MartialArtPrototype? style = null,
        MartialArtsKnowledgeComponent? comp = null)
    {
        var downed = _standing.IsDown(target);

        switch (effect)
        {
            case MartialArtConditionalEffect conditional:
                if (MatchesCondition(user, target, conditional.Condition))
                {
                    foreach (var nested in conditional.Effects)
                        ApplyEffect(user, target, nested, style, comp);
                }
                break;
            case MartialArtDamageEffect damage:
                if (damage.OnlyIfDowned && !downed)
                    return;
                _damage.TryChangeDamage(target, damage.Damage, damage.IgnoreResistances, origin: user);
                break;

            case MartialArtStaminaEffect stamina:
                if (stamina.OnlyIfDowned && !downed)
                    return;
                _stamina.TakeStaminaDamage(target, stamina.Amount, source: user, ignoreResist: stamina.IgnoreResistances);
                break;

            case MartialArtKnockdownEffect knockdown:
                _stun.TryKnockdown(target, knockdown.StunSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
                    autoStand: false, drop: knockdown.DropItems, force: true);
                break;

            case MartialArtStatusEffect status:
                if (status.OnlyIfDowned && !downed)
                    return;
                _status.TryAddStatusEffectDuration(target, status.Effect, TimeSpan.FromSeconds(status.Duration));
                break;

            case MartialArtDropItemEffect drop:
                TryDropOrSteal(user, target, drop.Steal);
                break;

            case MartialArtThrowEffect throwEffect:
                var direction = Transform(target).WorldPosition - Transform(user).WorldPosition;
                if (direction.LengthSquared() > 0.001f)
                    _throwing.TryThrow(target, direction, throwEffect.Speed, user);
                break;

            case MartialArtPopupEffect popup:
                _popup.PopupEntity(Loc.GetString(popup.Text,
                    ("user", Identity.Entity(user, EntityManager)),
                    ("target", Identity.Entity(target, EntityManager))), target, target, PopupType.LargeCaution);
                break;

            case MartialArtSoundEffect sound:
                _audio.PlayPvs(sound.Sound, target);
                break;

            case MartialArtSleepEffect sleep:
                if (sleep.OnlyIfStaminaCritical &&
                    (!TryComp<StaminaComponent>(target, out var staminaComp) || !staminaComp.Critical))
                    return;
                _status.TryAddStatusEffectDuration(target, "StatusEffectForcedSleeping", TimeSpan.FromSeconds(sleep.Duration));
                break;

            case MartialArtMuteEffect mute:
                _status.TryAddStatusEffectDuration(target, "StatusEffectMuted", TimeSpan.FromSeconds(mute.Duration));
                break;

            case MartialArtEyeDamageEffect eye:
                _blindable.AdjustEyeDamage(target, eye.Amount);
                break;

            case MartialArtStopPullEffect:
                if (TryComp<PullerComponent>(user, out var userPuller) && userPuller.Pulling == target &&
                    TryComp<PullableComponent>(target, out var pullable))
                {
                    _pulling.TryStopPull(target, pullable, user);
                }
                break;

            case MartialArtStopTargetPullEffect:
                if (TryComp<PullerComponent>(target, out var targetPuller) && targetPuller.Pulling == user &&
                    TryComp<PullableComponent>(user, out var userPullable))
                {
                    _pulling.TryStopPull(user, userPullable, target);
                }
                break;

            case MartialArtCounterEffect counter:
                if (style == null || comp == null)
                    break;

                if (comp.CounterExpires < _timing.CurTime)
                    comp.Counter = 0;

                if (counter.DamagePerStack != null && comp.Counter > 0)
                {
                    var damage = counter.DamagePerStack * comp.Counter;
                    _damage.TryChangeDamage(target, damage, origin: user);
                }

                comp.Counter = Math.Min(style.MaxCounter, comp.Counter + counter.Amount);
                comp.CounterExpires = _timing.CurTime + TimeSpan.FromSeconds(style.CounterWindow);
                break;
        }
    }

    private bool MatchesCondition(EntityUid user, EntityUid target, MartialArtCondition condition)
    {
        return condition switch
        {
            MartialArtCondition.TargetDowned => _standing.IsDown(target),
            MartialArtCondition.TargetStanding => !_standing.IsDown(target),
            MartialArtCondition.TargetStunned => HasComp<StunnedComponent>(target),
            MartialArtCondition.UserPullingTarget => TryComp<PullerComponent>(user, out var userPuller) &&
                                                    userPuller.Pulling == target,
            MartialArtCondition.TargetPullingUser => TryComp<PullerComponent>(target, out var targetPuller) &&
                                                    targetPuller.Pulling == user,
            _ => false,
        };
    }

    private void TryDropOrSteal(EntityUid user, EntityUid target, bool steal)
    {
        if (!TryComp<HandsComponent>(target, out var hands) ||
            !_hands.TryGetActiveItem((target, hands), out var item))
            return;

        if (!_hands.TryDrop((target, hands), item.Value))
            return;

        if (steal && _hands.TryGetEmptyHand(user, out var emptyHand))
            _hands.TryPickup(user, item.Value, emptyHand);
    }

    private void TryNeckSnap(EntityUid user, EntityUid target, MartialArtPrototype style, MartialArtsKnowledgeComponent comp)
    {
        if (!style.Necksnap)
            return;

        if (!_standing.IsDown(target) || _mobState.IsDead(target) ||
            !TryComp<PullerComponent>(user, out var puller) || puller.Pulling != target ||
            !TryComp<GrabStageComponent>(user, out var grab) || grab.Stage < GrabStage.Choke ||
            !TryComp<StaminaComponent>(target, out var stamina) || !stamina.Critical ||
            !_thresholds.TryGetDeadThreshold(target, out var threshold) || threshold is not { } dead)
            return;

        var damage = new DamageSpecifier(_prototypes.Index(BluntDamage), dead);
        if (!_damage.TryChangeDamage(target, damage, ignoreResistances: true, origin: user))
            return;

        comp.History.Clear();
        _audio.PlayPvs(new Robust.Shared.Audio.SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg"), target);
        _popup.PopupEntity(Loc.GetString("vg-martial-necksnap",
            ("user", Identity.Entity(user, EntityManager)),
            ("target", Identity.Entity(target, EntityManager))), target, target, PopupType.LargeCaution);

        if (TryComp<PullableComponent>(target, out var pullable))
            _pulling.TryStopPull(target, pullable, user);
    }

    #endregion

    #region Books

    private void OnBookUse(Entity<MartialArtsBookComponent> book, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (!IsValidReader(args.User))
        {
            _popup.PopupEntity(Loc.GetString("vg-martial-book-invalid-user"), args.User, args.User);
            args.Handled = true;
            return;
        }

        if (HasStyle(args.User, book.Comp.Style))
        {
            _popup.PopupEntity(Loc.GetString("vg-martial-book-already-known"), args.User, args.User);
            args.Handled = true;
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, args.User, book.Comp.LearnTime,
            new MartialArtsBookDoAfterEvent(), book.Owner, target: book.Owner, used: book.Owner)
        {
            NeedHand = true,
            BreakOnDropItem = true,
            BreakOnMove = false,
            BlockDuplicate = true,
            CancelDuplicate = true,
            RequireCanInteract = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            args.Handled = true;
    }

    private void OnBookLearned(Entity<MartialArtsBookComponent> book, ref MartialArtsBookDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var user = args.User;
        if (!IsValidReader(user) || !_interaction.InRangeUnobstructed(user, book.Owner, 2f))
            return;

        if (HasStyle(user, book.Comp.Style))
        {
            _popup.PopupEntity(Loc.GetString("vg-martial-book-already-known"), user, user);
            return;
        }

        if (!TryLearnStyle(user, book.Comp.Style, permanent: true))
        {
            _popup.PopupEntity(Loc.GetString("vg-martial-book-failed"), user, user);
            return;
        }

        if (book.Comp.LearnSound != null)
            _audio.PlayPvs(book.Comp.LearnSound, user);

        if (_prototypes.TryIndex(book.Comp.Style, out var style))
            _popup.PopupEntity(Loc.GetString("vg-martial-learned", ("style", Loc.GetString(style.Name))), user, user, PopupType.Medium);

        var coordinates = Transform(book).Coordinates;
        Spawn(book.Comp.AshPrototype.Id, coordinates);
        QueueDel(book);
        args.Handled = true;
    }

    private bool IsValidReader(EntityUid user)
    {
        return !TerminatingOrDeleted(user) && !_mobState.IsDead(user) && HasComp<HandsComponent>(user);
    }

    #endregion

    #region Equipment and shooting

    private void OnClothingEquipped(Entity<MartialArtsClothingComponent> clothing, ref GotEquippedEvent args)
    {
        if (args.EquipTarget == clothing.Owner)
            return;

        if (TryLearnStyle(args.EquipTarget, clothing.Comp.Style, permanent: false))
        {
            if (_prototypes.TryIndex(clothing.Comp.Style, out var style))
                _popup.PopupEntity(Loc.GetString("vg-martial-equipment-learned", ("style", Loc.GetString(style.Name))), args.EquipTarget, args.EquipTarget);
        }
    }

    private void OnClothingUnequipped(Entity<MartialArtsClothingComponent> clothing, ref GotUnequippedEvent args)
    {
        if (args.EquipTarget == clothing.Owner ||
            !TryComp<MartialArtsKnowledgeComponent>(args.EquipTarget, out var comp))
            return;

        ForgetStyle(args.EquipTarget, comp, clothing.Comp.Style);
    }

    private void OnShotAttempted(Entity<MartialArtsNoShootComponent> ent, ref ShotAttemptedEvent args)
    {
        if (args.User != ent.Owner)
            return;

        _popup.PopupEntity(Loc.GetString("vg-martial-cannot-shoot"), args.User, args.User);
        args.Cancel();
    }

    #endregion

    #region Helpers

    private bool IsValidTarget(EntityUid user, EntityUid target)
    {
        if (target == user || TerminatingOrDeleted(target))
            return false;

        if (!HasComp<HumanoidProfileComponent>(target) || !HasComp<MobStateComponent>(target))
            return false;

        return !_mobState.IsDead(target);
    }

    #endregion
}
