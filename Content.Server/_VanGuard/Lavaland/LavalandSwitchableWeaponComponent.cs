namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Adjusts a weapon's attack speed when its item toggle changes state.
/// Used by the telescopic cleaving saw.
/// </summary>
[RegisterComponent]
public sealed partial class LavalandSwitchableWeaponComponent : Component
{
    [DataField]
    public float AttackRateActivated = 1f;

    [DataField]
    public float AttackRateDeactivated = 2f;
}
