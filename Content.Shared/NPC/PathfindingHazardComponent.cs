namespace Content.Shared.NPC;

/// <summary>
/// Marks an entity as a tile hazard for ground NPC navigation. A mob that
/// cannot safely walk over this entity (e.g. lava or a chasm) will have
/// pathfinding refuse to route through any tile occupied by it.
/// </summary>
/// <remarks>
/// Nothing here decides which mobs are immune: that is left to
/// <see cref="Content.Server.NPC.Pathfinding.PathFlags.Hazards"/>, which is
/// granted automatically to airborne movers and can also be granted through
/// an NPC's <c>NavHazards</c> blackboard key. Keeping the marker passive
/// means any new hazardous tile just has to add the component and no
/// per-mob special-casing is needed.
/// </remarks>
[RegisterComponent]
public sealed partial class PathfindingHazardComponent : Component;