using Robust.Shared.GameStates;
using System.Numerics;

namespace Content.Shared._VanGuard.Tools;

[RegisterComponent, NetworkedComponent]
public sealed partial class WeldingSparksAnimationComponent : Component
{
    [DataField]
    public Vector2 StartingOffset;

    [DataField]
    public Vector2? EndingOffset;
}