using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Carrying;

/// <summary>
/// Marks the temporary, invisible strap entity that attaches a carried mob to
/// its carrier using the existing buckle system.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CarryAnchorComponent : Component
{
    public EntityUid Carrier;
}
