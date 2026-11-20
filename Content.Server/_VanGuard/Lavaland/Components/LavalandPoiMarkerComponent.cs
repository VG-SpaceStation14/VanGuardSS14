using Robust.Shared.Map.Components;

namespace Content.Server._VanGuard.Lavaland.Components;

/// <summary>
/// Marks a POI that has been folded into the planet's surface grid.
///
/// Lavaland POIs are authored as standalone map files. The loader turns each
/// of them into its own grid entity, which is then merged into the planet's
/// surface grid so the whole planet shares a single tile space. The grid
/// entity disappears in that merge, so this marker keeps the POI's identity
/// and footprint discoverable afterwards.
/// </summary>
[RegisterComponent]
public sealed partial class LavalandPoiMarkerComponent : Component
{
    /// <summary>
    /// Localization key of the POI's name, e.g. "lavaland-poi-blood-miner-arena".
    /// </summary>
    [DataField]
    public string Name = string.Empty;

    /// <summary>
    /// Localization key of the POI's survey fax designation.
    /// </summary>
    [DataField]
    public string FaxName = string.Empty;

    /// <summary>
    /// The POI's footprint in the surface grid's tile coordinates. The
    /// surface grid is the map entity itself, so these coordinates are also
    /// the planet's world tile coordinates.
    /// </summary>
    [DataField]
    public Box2 Bounds = Box2.Empty;

    /// <summary>
    /// True for the unique structures (arena, Legion base) rather than the
    /// ordinary dungeons.
    /// </summary>
    [DataField]
    public bool Unique;
}
