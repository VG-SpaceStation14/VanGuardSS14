using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Grab;

/// <summary>
/// Multiplies the time needed to reach a grab stage when worn.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GrabStageTimeModifierComponent : Component
{
    [DataField(required: true)]
    public Dictionary<GrabStage, float> Modifiers = new();
}
