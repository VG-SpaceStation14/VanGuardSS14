using Content.Shared.Damage;

namespace Content.Server._VanGuard.Mining.Melee;

/// <summary>
/// Adjusts damage based on the atmospheric pressure at the target: in a low-pressure or
/// vacuum environment melee weapons deal additional damage, while projectiles are weakened.
/// </summary>
[RegisterComponent]
public sealed partial class PressureDamageModifyComponent : Component
{
    /// <summary>
    /// Projectile damage multiplier applied outside the configured pressure range.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("projDamage")]
    public float ProjDamage = 0.1f;

    /// <summary>
    /// Pressure in kPa at or below which the damage modification kicks in.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("maxPressure")]
    public float MaxPressure = 40f;

    [ViewVariables(VVAccess.ReadWrite), DataField("minPressure")]
    public float MinPressure = 0f;

    /// <summary>
    /// Bonus damage applied by melee attacks when the target is in the configured pressure range.
    /// </summary>
    [DataField("additionalDamage")]
    public DamageSpecifier? AdditionalDamage;
}
