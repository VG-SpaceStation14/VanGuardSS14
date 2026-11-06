using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.MartialArts;

[Prototype]
public sealed partial class MartialArtPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    /// <summary>
    /// Moves that are available when this is the current style.
    /// </summary>
    [DataField]
    public List<ProtoId<MartialArtMovePrototype>> Moves = new();

    /// <summary>
    /// If false, this style prevents using ranged weapons while active.
    /// </summary>
    [DataField]
    public bool CanShoot = true;

    /// <summary>
    /// Probability of reflecting projectiles while this style is active.
    /// </summary>
    [DataField]
    public float ReflectProbability;

    /// <summary>
    /// Cooldown between two successful combo moves.
    /// </summary>
    [DataField]
    public float ComboCooldown = 0.5f;

    [DataField]
    public int MaxCounter;

    [DataField]
    public float CounterWindow = 6f;

    /// <summary>
    /// Extra unarmed damage granted by the style.
    /// </summary>
    [DataField]
    public DamageSpecifier? UnarmedBonusDamage;

    [DataField]
    public SoundSpecifier? LearnSound;

    /// <summary>
    /// Enables the CQC neck snap execution on a downed, stamina-critical grabbed target.
    /// </summary>
    [DataField]
    public bool Necksnap;
}

[Prototype]
public sealed partial class MartialArtMovePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public LocId Description;

    /// <summary>
    /// The combo sequence required for a passive move. Empty for an active move.
    /// </summary>
    [DataField]
    public List<MartialArtActionType> Sequence = new();

    /// <summary>
    /// Action prototype granted for an active move. Passive moves leave this null.
    /// </summary>
    [DataField]
    public EntProtoId? Action;

    [DataField]
    public float Cooldown = 1f;

    [DataField]
    public float Range = 1.5f;

    [DataField]
    public bool RequiresStandingTarget;

    [DataField]
    public bool RequiresDownedTarget;

    /// <summary>
    /// The target must be pulling the user.
    /// </summary>
    [DataField]
    public bool RequiresTargetPullingUser;

    /// <summary>
    /// The user must be pulling the target.
    /// </summary>
    [DataField]
    public bool RequiresUserPullingTarget;

    [DataField]
    public bool StopPull;

    [DataField]
    public List<IMartialArtEffect> Effects = new();

    public bool IsActive => Action != null;
}
