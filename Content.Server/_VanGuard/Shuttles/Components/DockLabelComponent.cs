using Robust.Shared.Localization;

namespace Content.Server._VanGuard.Shuttles.Components;

/// <summary>
/// Optional human-readable label for a docking target. Can be placed on the dock
/// itself or on the grid it belongs to.
/// </summary>
[RegisterComponent]
public sealed partial class DockLabelComponent : Component
{
    [DataField]
    public LocId? Label;
}
