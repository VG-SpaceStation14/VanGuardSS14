using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Tools;

/// <summary>
/// Per-target offsets used by the client to slide the spark effect along a welding seam.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WeldingSparksAnimationComponent : Component
{
    [DataField]
    public Vector2 StartingOffset;

    [DataField]
    public Vector2? EndingOffset;
}
