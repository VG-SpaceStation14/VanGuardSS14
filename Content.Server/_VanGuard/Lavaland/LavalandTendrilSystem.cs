using Content.Shared.Destructible;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Lavaland;

public sealed partial class LavalandTendrilSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;

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
        var query = AllEntityQuery<LavalandTendrilComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
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

    public bool TryDestroy(Entity<LavalandTendrilComponent> ent)
    {
        if (!Exists(ent.Owner))
            return false;
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
        _audio.PlayPvs(CollapseSound, coords);
        Timer.Spawn(TimeSpan.FromSeconds(ent.Comp.ChasmDelay), () => SpawnChasm(coords, ent.Comp.ChasmRadius));
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
