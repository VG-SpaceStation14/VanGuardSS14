using Content.Shared.Chasm;
using Content.Shared.Interaction.Events;
using Content.Shared.Teleportation.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Content.Shared.Pinpointer;
using Content.Shared.Warps;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Emergency Lavaland escape device. On use it opens a one-way wormhole to a
/// station beacon; when carried into a chasm it pulls the carrier through a
/// short-lived pair of wormholes before the fall resolves.
/// </summary>
public sealed partial class JaunterSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private LinkedEntitySystem _link = default!;

    private static readonly SoundSpecifier UseSound = new SoundPathSpecifier("/Audio/Effects/teleport_departure.ogg");
    private static readonly SoundSpecifier FallSound = new SoundPathSpecifier("/Audio/Items/Mining/fultext_launch.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JaunterComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ChasmComponent, EntityStartFallingAttemptEvent>(OnStartFallingAttempt);
    }

    private void OnUseInHand(Entity<JaunterComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || !TryGetBeacon(args.User, out var beacon))
            return;

        var portal = Spawn("JaunterPortal", Transform(args.User).Coordinates);
        _link.OneWayLink(portal, beacon, deleteOnEmptyLinks: true);
        _audio.PlayPvs(UseSound, Transform(args.User).Coordinates);
        Consume(ent, args.User);
        args.Handled = true;
    }

    private void OnStartFallingAttempt(Entity<ChasmComponent> chasm, ref EntityStartFallingAttemptEvent args)
    {
        if (args.Cancelled || !TryFindJaunter(args.Faller, out var jaunter))
            return;

        if (!TryGetBeacon(args.Faller, out var beacon))
            return;

        var origin = Transform(args.Faller).Coordinates;
        var destination = Transform(beacon).Coordinates;
        Spawn("JaunterPortal", origin);
        Spawn("JaunterPortal", destination);
        _audio.PlayPvs(FallSound, origin);
        _transform.SetCoordinates(args.Faller, destination);
        _transform.AttachToGridOrMap(args.Faller, Transform(args.Faller));
        Consume(jaunter, args.Faller);
        args.Cancelled = true;
    }

    private void Consume(Entity<JaunterComponent> jaunter, EntityUid user)
    {
        jaunter.Comp.Uses--;
        if (jaunter.Comp.DeleteOnUse || jaunter.Comp.Uses <= 0)
            QueueDel(jaunter.Owner);
    }

    private bool TryGetBeacon(EntityUid user, out EntityUid beacon)
    {
        beacon = default;
        var candidates = new List<EntityUid>();
        var query = AllEntityQuery<WarpPointComponent, NavMapBeaconComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var nav, out var xform))
        {
            if (!nav.Enabled || xform.MapUid == null)
                continue;

            if (TryComp<LavalandOutpostRuleComponent>(xform.MapUid.Value, out _))
                continue;

            candidates.Add(uid);
        }

        if (candidates.Count == 0)
            return false;

        beacon = _random.Pick(candidates);
        return true;
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
