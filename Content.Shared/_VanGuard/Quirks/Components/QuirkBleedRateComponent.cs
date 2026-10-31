using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Quirks.Components;

/// <summary>
///     Scales the bleeding rate of a mob that has picked a corresponding character quirk.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class QuirkBleedRateComponent : Component
{
    /// <summary>
    ///     Multiplier applied to the amount of blood lost while bleeding.
    ///     Values below one slow the bleeding down, values above one make it worse.
    /// </summary>
    [DataField]
    public float BleedMultiplier = 1f;
}
