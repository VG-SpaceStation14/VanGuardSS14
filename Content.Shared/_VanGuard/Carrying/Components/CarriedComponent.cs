using Robust.Shared.GameStates;
using Robust.Shared.Maths;

namespace Content.Shared._VanGuard.Carrying;

/// <summary>
/// Marks a character that is currently being carried. The actual attachment is
/// owned by the buckle system through a temporary carry anchor.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CarriedComponent : Component
{
    [AutoNetworkedField]
    public EntityUid? Carrier;

    [AutoNetworkedField]
    public EntityUid? Anchor;

    public bool Ending;

    /// <summary>
    /// The target's original horizontal sprite rotation, restored on release.
    /// </summary>
    public Angle OriginalHorizontalRotation;
}
