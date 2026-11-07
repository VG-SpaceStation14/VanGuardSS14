using Content.Shared._VanGuard.MartialArts;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._VanGuard.WeaponCombos;

[NetSerializable, Serializable]
public enum WeaponComboStance : byte
{
    Protective,
    Offensive,
}

[NetSerializable, Serializable]
public enum WeaponComboVisuals : byte
{
    State,
}

[NetSerializable, Serializable]
public enum WeaponComboAction : byte
{
    ProtectiveLight,
    ProtectiveHeavy,
    OffensiveLight,
    OffensiveHeavy,
}

[DataDefinition]
public sealed partial class WeaponComboMove
{
    [DataField]
    public List<WeaponComboAction> Actions = new();

    [DataField]
    public List<IMartialArtEffect> Effects = new();
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WeaponComboComponent : Component
{
    [DataField]
    public List<WeaponComboMove> Moves = new();

    [AutoNetworkedField]
    public WeaponComboStance Stance = WeaponComboStance.Protective;

    [DataField]
    public SoundSpecifier? SwapSound;

    public readonly List<WeaponComboAttack> History = new();
    public TimeSpan NextComboAt;
}

public readonly record struct WeaponComboAttack(WeaponComboAction Action, EntityUid Target, TimeSpan At);
