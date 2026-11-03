using Content.Shared.Destructible;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Lavaland;

public sealed partial class LavalandTendrilSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private LavalandFeatureSystem _features = default!;

    private TimeSpan _nextPlacementCheck;

    private static readonly SoundSpecifier CrackSound = new SoundPathSpecifier("/Audio/Effects/glass_crack1.ogg");
    private static readonly SoundSpecifier WarningSound = new SoundPathSpecifier("/Audio/Effects/explosion_small1.ogg");
    private static readonly SoundSpecifier CollapseSound = new SoundPathSpecifier("/Audio/Effects/explosion1.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LavalandTendrilComponent, DestructionEventArgs>(OnDestroyed);
        SubscribeLocalEvent<LavalandTendrilComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<LavalandTendrilMobComponent, MobStateChangedEvent>(OnMobState);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime >= _nextPlacementCheck)
        {
            _nextPlacementCheck = _timing.CurTime + TimeSpan.FromSeconds(1);
            ValidatePlacements();
        }

        var query = AllEntityQuery<LavalandTendrilComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Destroyed && TryComp<Content.Shared.Damage.Components.DamageableComponent>(uid, out var damageable) &&
                _damageable.GetTotalDamage((uid, damageable)) >= 250)
            {
                comp.Destroyed = true;
                DestroyCore((uid, comp));
                QueueDel(uid);
                continue;
            }

            comp.Mobs.RemoveAll(mob => !Exists(mob));
            if (comp.Mobs.Count >= comp.MaxSpawns)
                continue;

            if (!TryComp<LavalandTendrilSpawnTimerComponent>(uid, out var timer))
            {
                timer = EnsureComp<LavalandTendrilSpawnTimerComponent>(uid);
                timer.NextSpawn = _timing.CurTime + TimeSpan.FromSeconds(comp.SpawnDelay);
                continue;
            }

            if (timer.NextSpawn > _timing.CurTime)
                continue;

            timer.NextSpawn = _timing.CurTime + TimeSpan.FromSeconds(comp.SpawnDelay);
            var coords = Transform(uid).Coordinates.Offset(_random.NextVector2(3));
            var mob = Spawn(_random.Pick(comp.Spawns), coords);
            EnsureComp<LavalandTendrilMobComponent>(mob).Tendril = uid;
            comp.Mobs.Add(mob);
        }
    }

    /// <summary>
    /// A late safety net for lazily generated terrain: if a tendril ends up on
    /// lava, a chasm or another invalid tile, move it to a nearby valid tile or
    /// remove it when no valid tile exists.
    /// </summary>
    private void ValidatePlacements()
    {
        var query = AllEntityQuery<LavalandTendrilComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid is not { } mapUid || !TryComp<MapGridComponent>(mapUid, out var grid))
                continue;

            var indices = _map.TileIndicesFor(mapUid, grid, xform.Coordinates);
            if (_features.TryGetValidSpawn(mapUid, grid, indices, out _, out _, uid))
                continue;

            var relocated = false;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var offset = new Vector2i(_random.Next(-2, 3), _random.Next(-2, 3));
                if (!_features.TryGetValidSpawn(mapUid, grid, indices + offset, out var coords, out var tile))
                    continue;

                _features.PrepareSpawnTile(mapUid, grid, indices + offset, tile);
                _transform.SetCoordinates(uid, coords);
                _transform.AnchorEntity(uid);
                relocated = true;
                break;
            }

            if (!relocated)
                QueueDel(uid);
        }
    }

    public bool TryDestroy(Entity<LavalandTendrilComponent> ent)
    {
        if (!Exists(ent.Owner) || ent.Comp.Destroyed)
            return false;
        ent.Comp.Destroyed = true;
        DestroyCore(ent);
        QueueDel(ent.Owner);
        return true;
    }

    private void OnDestroyed(Entity<LavalandTendrilComponent> ent, ref DestructionEventArgs args)
    {
        if (ent.Comp.Destroyed)
            return;
        ent.Comp.Destroyed = true;
        DestroyCore(ent);
    }

    private void OnDamageChanged(Entity<LavalandTendrilComponent> ent, ref DamageChangedEvent args)
    {
        if (ent.Comp.Destroyed || _damageable.GetTotalDamage((ent.Owner, args.Damageable)) < 250)
            return;

        ent.Comp.Destroyed = true;
        DestroyCore(ent);
        QueueDel(ent.Owner);
    }

    private void DestroyCore(Entity<LavalandTendrilComponent> ent)
    {
        var coords = Transform(ent.Owner).Coordinates;
        Spawn(ent.Comp.Loot, coords);

        // A deliberate three-stage warning: cracking shell, growing rumble,
        // then the actual collapse when the chasm opens.
        _audio.PlayPvs(CrackSound, coords);
        Timer.Spawn(TimeSpan.FromSeconds(1.5), () => _audio.PlayPvs(WarningSound, coords));
        Timer.Spawn(TimeSpan.FromSeconds(ent.Comp.ChasmDelay), () =>
        {
            _audio.PlayPvs(CollapseSound, coords);
            SpawnChasm(coords, ent.Comp.ChasmRadius);
        });
    }

    private void SpawnChasm(EntityCoordinates origin, int radius)
    {
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
                Spawn("FloorChasmEntity", origin.Offset(new System.Numerics.Vector2(x, y)));
        }
    }

    private void OnMobState(Entity<LavalandTendrilMobComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || ent.Comp.Tendril is not { } tendril)
            return;

        if (TryComp<LavalandTendrilComponent>(tendril, out var comp))
            comp.Mobs.Remove(ent.Owner);
    }
}

[RegisterComponent]
public sealed partial class LavalandTendrilSpawnTimerComponent : Component
{
    public TimeSpan NextSpawn;
}
