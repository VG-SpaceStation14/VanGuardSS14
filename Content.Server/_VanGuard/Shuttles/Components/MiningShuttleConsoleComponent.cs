using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Shuttles.Components;

public enum MiningShuttleConsoleMode : byte
{
    Mining,
    Guest,
}

/// <summary>
/// A console for a docking-only mining shuttle. It offers exactly two
/// destinations: the owning station and the Lavaland outpost.
/// </summary>
[RegisterComponent]
public sealed partial class MiningShuttleConsoleComponent : Component
{
    /// <summary>
    /// Priority dock tag. If no dock with this tag is free, the stock docking
    /// system may use another compatible free dock on the destination grid.
    /// </summary>
    [DataField]
    public MiningShuttleConsoleMode Mode = MiningShuttleConsoleMode.Mining;

    [DataField]
    public ProtoId<TagPrototype> DockTag = "DockMining";

    /// <summary>
    /// Optional explicit shuttle link. When null, the console uses its own
    /// shuttle grid or discovers the marked mining shuttle.
    /// </summary>
    [DataField]
    public EntityUid? LinkedShuttle;
}
