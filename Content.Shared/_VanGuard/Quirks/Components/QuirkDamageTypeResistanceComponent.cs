using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Quirks.Components;

/// <summary>
///     Multiplies incoming damage of specific damage types for a mob that has picked a corresponding
///     character quirk. Only damage types listed in <see cref="Modifiers"/> are affected.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class QuirkDamageTypeResistanceComponent : Component
{
    /// <summary>
    ///     Maps a damage type id (for example <c>Cold</c>) to the multiplier applied to incoming damage of that type.
    ///     Values below one reduce the damage, values above one increase it.
    /// </summary>
    [DataField]
    public Dictionary<string, float> Modifiers = new();
}
