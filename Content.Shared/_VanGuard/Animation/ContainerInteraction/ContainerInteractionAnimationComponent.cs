using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Animation.ContainerInteraction;

[RegisterComponent, NetworkedComponent]
public sealed partial class ContainerInteractionAnimationComponent : Component
{
    [DataField]
    public float Scale = 1.1f;

    [DataField]
    public float ScaleVariation = 0.15f;

    [DataField]
    public float Duration = 0.2f;
}