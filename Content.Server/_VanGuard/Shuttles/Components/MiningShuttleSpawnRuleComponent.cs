using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._VanGuard.Shuttles.Components;

/// <summary>
/// Round-start rule that loads the mining shuttle and docks it to a tagged station
/// dock. If the station has no free matching dock it falls back to the Lavaland
/// outpost dock and logs a warning.
/// </summary>
[RegisterComponent]
public sealed partial class MiningShuttleSpawnRuleComponent : Component
{
    [DataField]
    public ResPath ShuttlePath = new("/Maps/_VanGuard/Shuttles/NT - Mining");

    [DataField]
    public ProtoId<TagPrototype> DockTag = "DockMining";

    [DataField]
    public string ShuttleName = "mining-shuttle-name";

    /// <summary>
    /// Runtime reference to the loaded shuttle grid, used by tests and diagnostics.
    /// </summary>
    [ViewVariables]
    public EntityUid? ShuttleGrid;

    /// <summary>
    /// True when the shuttle was docked to the Lavaland outpost because no station
    /// dock was available.
    /// </summary>
    [ViewVariables]
    public bool DockedToFallback;
}
