using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Shuttles.Components;

/// <summary>
/// A shuttle console that only lists tagged docking ports and sends the shuttle
/// directly to one of them. It deliberately does not expose free-flight controls.
/// </summary>
[RegisterComponent]
public sealed partial class MiningShuttleConsoleComponent : Component
{
    /// <summary>
    /// Only docks carrying this priority-dock tag are offered as destinations.
    /// </summary>
    [DataField]
    public ProtoId<TagPrototype> DockTag = "DockMining";

    /// <summary>
    /// Optional explicit shuttle link. When null, the console controls the shuttle
    /// grid it is standing on. A future call console can set this while remaining
    /// on the station.
    /// </summary>
    [DataField]
    public EntityUid? LinkedShuttle;
}
