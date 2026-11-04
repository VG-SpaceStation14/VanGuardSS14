using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Shared.Chasm;
using Content.Shared.Interaction;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Handles the Blood Miner's second-phase teleport behaviour. The boss keeps
/// its generic Goliath HTN and ranged action; this system only adds the blink
/// that the old build expressed through a weighted teleport action.
/// </summary>
public sealed partial class LavalandBloodMinerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private static readonly SoundSpecifier BlinkSound =
        new SoundPathSpecifier("/Audio/Effects/teleport_departure.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LavalandBloodMinerComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<LavalandBloodMinerComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextTeleport = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.TeleportInterval);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<LavalandBloodMinerComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var bloodMiner, out var htn))
        {
            if (_timing.CurTime < bloodMiner.NextTeleport)
                continue;

            if (!htn.Blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager))
                continue;

            bloodMiner.NextTeleport = _timing.CurTime + TimeSpan.FromSeconds(bloodMiner.TeleportInterval);
            var targetCoords = Transform(target).Coordinates;

            for (var attempt = 0; attempt < 20; attempt++)
            {
                var offset = _random.NextVector2(4f);
                var candidate = targetCoords.Offset(offset);
                if (!_interaction.InRangeUnobstructed(uid, candidate, -1f))
                    continue;

                if (_lookup.GetEntitiesInRange<ChasmComponent>(candidate, 1f).Count > 0)
                    continue;

                _transform.SetCoordinates(uid, candidate);
                _transform.AttachToGridOrMap(uid, Transform(uid));
                _audio.PlayPvs(BlinkSound, uid);
                break;
            }
        }
    }
}
