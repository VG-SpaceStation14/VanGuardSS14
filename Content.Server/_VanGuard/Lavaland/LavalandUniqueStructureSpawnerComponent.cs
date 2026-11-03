using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Spawns a guaranteed set of unique structures on the Lavaland map. Every
/// structure entry selects exactly one of its map variants, so a structure can
/// never exist more than once on the same map.
/// </summary>
[RegisterComponent]
public sealed partial class LavalandUniqueStructureSpawnerComponent : Component
{
    [DataField]
    public List<LavalandUniqueStructure> Structures = new();
}

[DataDefinition]
public sealed partial class LavalandUniqueStructure
{
    /// <summary>
    /// Stable identifier used to prevent duplicate placement on one map.
    /// </summary>
    [DataField(required: true)]
    public string Id = string.Empty;

    /// <summary>
    /// Localization key used for the POI fax entry.
    /// </summary>
    [DataField(required: true)]
    public LocId Name = string.Empty;

    /// <summary>
    /// Map variants. Exactly one is picked per map.
    /// </summary>
    [DataField(required: true)]
    public List<ResPath> Variants = new();

    [DataField]
    public float MinDistance = 100f;

    [DataField]
    public float MaxDistance = 220f;

    [DataField]
    public float ClearRadius = 18f;
}
