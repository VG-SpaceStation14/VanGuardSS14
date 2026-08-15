using Content.Shared.Damage;

namespace Content.Shared._VanGuard.Mining.Melee;

/// <summary>
/// Grants bonus damage when attacking an entity from behind.
/// </summary>
[RegisterComponent]
public sealed partial class BackstabDamageMultipilierComponent : Component
{
    [DataField]
    public DamageSpecifier BonusDamage = new();
}
