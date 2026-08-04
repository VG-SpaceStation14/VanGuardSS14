using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Animation.Storage;

[RegisterComponent, NetworkedComponent]
public sealed partial class ClosetInteractionAnimationComponent : Component
{
    [DataField]
    public float Scale = 1.05f;

    [DataField]
    public float ScaleVariation = 0.15f;

    [DataField]
    public float Duration = 0.2f;
}