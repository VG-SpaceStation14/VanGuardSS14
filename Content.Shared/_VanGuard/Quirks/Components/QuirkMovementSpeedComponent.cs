using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Quirks.Components;

/// <summary>
///     Scales the movement speed of a mob that has picked a corresponding character quirk.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class QuirkMovementSpeedComponent : Component
{
    /// <summary>
    ///     Multiplier applied to the walking speed.
    /// </summary>
    [DataField]
    public float WalkModifier = 1f;

    /// <summary>
    ///     Multiplier applied to the sprinting speed.
    /// </summary>
    [DataField]
    public float SprintModifier = 1f;
}
