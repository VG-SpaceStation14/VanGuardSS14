using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Shuttles.Components;

/// <summary>
/// Marks the mining shuttle grid. The station is resolved from station membership
/// when the shuttle needs a destination.
/// </summary>
[RegisterComponent]
public sealed partial class MiningShuttleComponent : Component
{
    [DataField]
    public ProtoId<TagPrototype> DockTag = "DockMining";

    [ViewVariables]
    public EntityUid? Station;
}
