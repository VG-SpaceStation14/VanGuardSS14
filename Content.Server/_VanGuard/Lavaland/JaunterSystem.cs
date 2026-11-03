using Content.Shared.Chasm;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Medical;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Pinpointer;
using Content.Shared.Teleportation.Systems;
using Content.Shared.Warps;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;
using Robust.Shared.Spawners;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Emergency Lavaland escape device.
/// <para>
/// Using it opens a short-lived one-way wormhole to a beacon outside the
/// Lavaland map. Being carried into a chasm first plays out the normal fall;
/// only once the fall completes does the jaunter pull the carrier through a
/// wormhole to safety, at the cost of stamina and a bout of vomiting.
/// </para>
/// </summary>
public sealed partial class JaunterSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private LinkedEntitySystem _link = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private VomitSystem _vomit = default!;

    private static readonly SoundSpecifier TeleportSound =
        new SoundPathSpecifier("/Audio/Items/Mining/fultext_launch.ogg");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JaunterComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ChasmComponent, EntityCompletedFallingIntoChasmEvent>(OnCompletedFalling);
        SubscribeLocalEvent<JaunterPortalComponent, StartCollideEvent>(OnPortalCollide);
        SubscribeLocalEvent<JaunterKillPortalComponent, StartCollideEvent>(OnKillPortalCollide);
    }

    private void OnUseInHand(Entity<JaunterComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var origin = Transform(args.User).Coordinates;

        if (!TryGetBeacon(out var beacon))
        {
            // There is nowhere safe to go. The device still burns out and
            // leaves a lethal dead-end wormhole behind.
            SpawnKillPortal(origin);
            Consume(ent);
            return;
        }

        var spawnAt = FindFreeNearby(origin, 2f) ?? origin;
        var portal = Spawn("JaunterPortal", spawnAt);
        _link.OneWayLink(portal, beacon, deleteOnEmptyLinks: false);
        Consume(ent);
    }

    /// <summary>
    /// The jaunter does not interrupt the fall. It waits for the normal chasm
    /// fall to finish and only then cancels the deletion and teleports the
    /// carrier away.
    /// </summary>
    private void OnCompletedFalling(Entity<ChasmComponent> chasm, ref EntityCompletedFallingIntoChasmEvent args)
    {
        if (args.Cancelled)
            return;

        var faller = args.Faller.Owner;
        if (!TryFindJaunter(faller, out var jaunter))
            return;

        EntityCoordinates destination;
        if (TryGetBeacon(out var beacon))
            destination = Transform(beacon).Coordinates;
        else if (FindSafeNearby(faller) is { } safe)
            destination = safe;
        else
            return;

        _transform.SetCoordinates(faller, destination);
        _transform.AttachToGridOrMap(faller, Transform(faller));
        _audio.PlayPvs(TeleportSound, faller);
        DrainStamina(faller);
        TryVomit(faller);
        Consume(jaunter);

        args.Cancelled = true;
    }

    private void OnPortalCollide(Entity<JaunterPortalComponent> portal, ref StartCollideEvent args)
    {
        var subject = args.OtherEntity;

        // The destination side already has the portal's own arrival sound.
        // This is the distinctive jaunter departure crack.
        _audio.PlayPvs(TeleportSound, portal.Owner);
        DrainStamina(subject);
        TryVomit(subject);

        // A used wormhole collapses shortly after the first traveller.
        if (TryComp<TimedDespawnComponent>(portal.Owner, out var despawn))
            despawn.Lifetime = MathF.Min(despawn.Lifetime, portal.Comp.AfterEnterLifetime);
        else
            EnsureComp<TimedDespawnComponent>(portal.Owner).Lifetime = portal.Comp.AfterEnterLifetime;
    }

    private void OnKillPortalCollide(Entity<JaunterKillPortalComponent> portal, ref StartCollideEvent args)
    {
        var subject = args.OtherEntity;
        if (subject == portal.Owner)
            return;

        // A failed jaunter wormhole is a dead end: it tears the traveller apart.
        QueueDel(subject);
    }

    private void SpawnKillPortal(EntityCoordinates origin)
    {
        var spawnAt = FindFreeNearby(origin, 2f) ?? origin;
        Spawn("JaunterKillPortal", spawnAt);
    }

    private void DrainStamina(EntityUid target)
    {
        if (!TryComp<StaminaComponent>(target, out var stamina))
            return;

        // Push the target all the way into stamina crit: the wormhole leaves
        // the traveller winded and knocked down.
        var damage = MathF.Max(0.01f, stamina.CritThreshold - stamina.StaminaDamage);
        _stamina.TakeStaminaDamage(target, damage, stamina, ignoreResist: true);
    }

    private void TryVomit(EntityUid target)
    {
        _vomit.Vomit(target);
    }

    private void Consume(Entity<JaunterComponent> jaunter)
    {
        jaunter.Comp.Uses--;
        if (jaunter.Comp.DeleteOnUse || jaunter.Comp.Uses <= 0)
            QueueDel(jaunter.Owner);
    }

    private bool TryGetBeacon(out EntityUid beacon)
    {
        beacon = default;

        var excludedMaps = new HashSet<MapId>();
        var rules = AllEntityQuery<LavalandOutpostRuleComponent>();
        while (rules.MoveNext(out var rule))
        {
            if (rule.MapId is { } mapId)
                excludedMaps.Add(mapId);
        }

        var candidates = new List<EntityUid>();
        var query = AllEntityQuery<WarpPointComponent, NavMapBeaconComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var nav, out var xform))
        {
            if (!nav.Enabled || xform.MapID == MapId.Nullspace)
                continue;

            if (excludedMaps.Contains(xform.MapID))
                continue;

            // Procedurally generated planets (including Lavaland) always have a
            // biome on the map entity. The outpost must never substitute for
            // the main station's beacon.
            if (xform.MapUid is { } mapUid && HasComp<BiomeComponent>(mapUid))
                continue;

            candidates.Add(uid);
        }

        if (candidates.Count == 0)
            return false;

        beacon = _random.Pick(candidates);
        return true;
    }

    private EntityCoordinates? FindSafeNearby(EntityUid target)
    {
        var origin = Transform(target).Coordinates;
        for (var i = 0; i < 30; i++)
        {
            var candidate = new EntityCoordinates(
                origin.EntityId,
                origin.X + _random.NextFloat(-5f, 5f),
                origin.Y + _random.NextFloat(-5f, 5f));

            if (!_interaction.InRangeUnobstructed(target, candidate, -1f))
                continue;

            if (_lookup.GetEntitiesInRange<ChasmComponent>(candidate, 1f).Count > 0)
                continue;

            return candidate;
        }

        return null;
    }

    private EntityCoordinates? FindFreeNearby(EntityCoordinates origin, float radius)
    {
        var candidates = new List<EntityCoordinates>();
        for (var dx = -MathF.Ceiling(radius); dx <= radius; dx++)
        {
            for (var dy = -MathF.Ceiling(radius); dy <= radius; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                if (dx * dx + dy * dy > radius * radius)
                    continue;

                candidates.Add(new EntityCoordinates(origin.EntityId, origin.X + dx, origin.Y + dy));
            }
        }

        _random.Shuffle(candidates);

        foreach (var candidate in candidates)
        {
            var occupied = false;
            foreach (var entity in _lookup.GetEntitiesInRange(candidate, 0.2f))
            {
                if (!TryComp<PhysicsComponent>(entity, out var physics) || !physics.CanCollide)
                    continue;

                occupied = true;
                break;
            }

            if (!occupied)
                return candidate;
        }

        return null;
    }

    private bool TryFindJaunter(EntityUid root, out Entity<JaunterComponent> jaunter)
    {
        jaunter = default;

        if (TryComp<JaunterComponent>(root, out var direct))
        {
            jaunter = (root, direct);
            return true;
        }

        if (!TryComp<ContainerManagerComponent>(root, out var containers))
            return false;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                if (TryFindJaunter(contained, out jaunter))
                    return true;
            }
        }

        return false;
    }
}
