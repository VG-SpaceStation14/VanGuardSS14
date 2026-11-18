using Content.Shared.FixedPoint;
using Content.Server._VanGuard.Lavaland.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using Robust.Shared.Log;

namespace Content.Server._VanGuard.Lavaland.Systems;

public sealed partial class ResurrectionCrystalSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _logManager = default!;

    private ISawmill _sawmill = default!;

    private static readonly SoundSpecifier BeginSound = new SoundCollectionSpecifier("GlassCrack");
    private static readonly SoundSpecifier CompleteSound = new SoundCollectionSpecifier("GlassBreak");
    private static readonly SoundSpecifier BrokenSound = new SoundCollectionSpecifier("WindowShatter");

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("resurrection_crystal");
        SubscribeLocalEvent<ResurrectionCrystalComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ActiveResurrectionCrystalComponent, DestructionEventArgs>(OnCrystalDestroyed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActiveResurrectionCrystalComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_timing.CurTime < comp.ReviveAt)
                continue;
            TryRevive((uid, comp));
        }
    }

    private void OnAfterInteract(Entity<ResurrectionCrystalComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (!TryComp<MobStateComponent>(target, out var mobState) || mobState.CurrentState != MobState.Dead)
        {
            _popup.PopupEntity(Loc.GetString("resurrection-crystal-alive"), ent, args.User);
            return;
        }

        if (Transform(target).GridUid == null)
        {
            _popup.PopupEntity(Loc.GetString("resurrection-crystal-no-floor"), ent, args.User);
            return;
        }

        var crystal = Spawn(ent.Comp.ActiveCrystalPrototype, Transform(target).Coordinates);
        var activeComp = Comp<ActiveResurrectionCrystalComponent>(crystal);
        var container = _container.EnsureContainer<Container>(crystal, ActiveResurrectionCrystalComponent.BodyContainer);
        if (!_container.Insert(target, container, force: true))
        {
            _sawmill.Error($"Failed to insert {ToPrettyString(target)} into resurrection crystal container.");
            QueueDel(crystal);
            return;
        }

        activeComp.HeldEntity = target;
        activeComp.ReviveAt = _timing.CurTime + activeComp.ReviveTime;

        args.Handled = true;
        QueueDel(ent.Owner);

        _audio.PlayPvs(BeginSound, crystal);
        _popup.PopupEntity(Loc.GetString("resurrection-crystal-begin"), target, args.User);
    }

    private void TryRevive(Entity<ActiveResurrectionCrystalComponent> ent)
    {
        if (ent.Comp.HeldEntity is not { } target || !Exists(target))
        {
            QueueDel(ent.Owner);
            return;
        }

        if (_container.TryGetContainer(ent.Owner, ActiveResurrectionCrystalComponent.BodyContainer, out var container))
        {
            if (!_container.Remove(target, container, force: true))
            {
                _sawmill.Error($"Failed to remove {ToPrettyString(target)} from resurrection crystal {ToPrettyString(ent.Owner)}.");
                return;
            }
        }

        if (TryComp<DamageableComponent>(target, out var damageable))
            _damageable.SetAllDamage((target, damageable), FixedPoint2.Zero);

        if (TryComp<MobThresholdsComponent>(target, out var thresholds))
        {
            var allowRevives = thresholds.AllowRevives;
            _mobThreshold.SetAllowRevives(target, true, thresholds);
            _mobThreshold.SetAllowRevives(target, allowRevives, thresholds);
        }
        else if (TryComp<MobStateComponent>(target, out var simpleMobState))
        {
            _mobState.ChangeMobState(target, MobState.Alive, simpleMobState, ent.Owner);
        }

        _audio.PlayPvs(CompleteSound, target);
        _popup.PopupEntity(Loc.GetString("resurrection-crystal-complete"), target, target);
        QueueDel(ent.Owner);
    }

    private void OnCrystalDestroyed(Entity<ActiveResurrectionCrystalComponent> ent, ref DestructionEventArgs args)
    {
        if (ent.Comp.HeldEntity is { } target && Exists(target))
        {
            if (_container.TryGetContainer(ent.Owner, ActiveResurrectionCrystalComponent.BodyContainer, out var container))
            {
                if (!_container.Remove(target, container, force: true))
                {
                    _sawmill.Error($"Failed to remove {ToPrettyString(target)} from destroyed resurrection crystal {ToPrettyString(ent.Owner)}.");
                    _container.EmptyContainer(container, destination: Transform(ent.Owner).Coordinates);
                }
            }

            _audio.PlayPvs(BrokenSound, ent.Owner);
            _popup.PopupCoordinates(
                Loc.GetString("resurrection-crystal-broken"),
                Transform(ent.Owner).Coordinates,
                PopupType.LargeCaution);
        }
        QueueDel(ent.Owner);
    }
}
