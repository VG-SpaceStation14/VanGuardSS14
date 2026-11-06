using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.MartialArts;

/// <summary>
/// A declarative effect applied by a martial arts move. Effects are executed by the server.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public partial interface IMartialArtEffect;

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MartialArtDamageEffect : IMartialArtEffect
{
    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    [DataField]
    public bool IgnoreResistances;

    [DataField]
    public bool OnlyIfDowned;
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MartialArtStaminaEffect : IMartialArtEffect
{
    [DataField(required: true)]
    public float Amount;

    [DataField]
    public bool OnlyIfDowned;

    [DataField]
    public bool IgnoreResistances;
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MartialArtKnockdownEffect : IMartialArtEffect
{
    [DataField]
    public bool DropItems;

    [DataField]
    public float? StunSeconds;
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MartialArtStatusEffect : IMartialArtEffect
{
    [DataField(required: true)]
    public EntProtoId Effect;

    [DataField]
    public float Duration = 1f;

    [DataField]
    public bool OnlyIfDowned;
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MartialArtDropItemEffect : IMartialArtEffect
{
    /// <summary>
    /// If true, try to put the dropped item into the attacker's free hand.
    /// </summary>
    [DataField]
    public bool Steal;
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MartialArtThrowEffect : IMartialArtEffect
{
    [DataField]
    public float Speed = 7f;
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MartialArtPopupEffect : IMartialArtEffect
{
    [DataField(required: true)]
    public LocId Text;
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MartialArtSoundEffect : IMartialArtEffect
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MartialArtSleepEffect : IMartialArtEffect
{
    [DataField]
    public float Duration = 1f;

    [DataField]
    public bool OnlyIfStaminaCritical;
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MartialArtMuteEffect : IMartialArtEffect
{
    [DataField]
    public float Duration = 10f;
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MartialArtEyeDamageEffect : IMartialArtEffect
{
    [DataField]
    public int Amount = 1;
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MartialArtStopPullEffect : IMartialArtEffect;

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MartialArtCounterEffect : IMartialArtEffect
{
    [DataField]
    public int Amount = 1;

    /// <summary>
    /// Additional damage per current counter stack.
    /// </summary>
    [DataField]
    public DamageSpecifier? DamagePerStack;
}
