using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Fired by <see cref="Content.Server._VanGuard.Lavaland.LavalandOutpostRuleSystem"/>
/// after Lavaland's planet, biomes, POIs and unique structures have all been
/// spawned. Subscribers must place the mining outpost in the same tick as this
/// event — the queue is processed at the start of the next tick, so all
/// previously queued deletions and physics ticks have already completed.
/// </summary>
/// <remarks>
/// This event exists because large dungeon POIs (lava_river.yml in particular)
/// briefly overlap the outpost in their authored YAML position before the
/// spawner's placement check rejects them. Loading the outpost grid only
/// <em>after</em> that rejection phase guarantees the outpost never coexists
/// with a transient grid at the same world coordinates.
/// </remarks>
public sealed class LavalandOutpostSpawnerFinishedEvent : EntityEventArgs
{
    public EntityUid MapUid { get; }
    public MapId MapId { get; }
    public Vector2 Origin { get; }
    public Box2 OutpostBounds { get; }

    public LavalandOutpostSpawnerFinishedEvent(
        EntityUid mapUid,
        MapId mapId,
        Vector2 origin,
        Box2 outpostBounds)
    {
        MapUid = mapUid;
        MapId = mapId;
        Origin = origin;
        OutpostBounds = outpostBounds;
    }
}
