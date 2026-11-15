using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
///     Item component. Activating on a dead mob spawns the active crystal and consumes the item.
/// </summary>
[RegisterComponent]
public sealed partial class ResurrectionCrystalComponent : Component
{
    [DataField]
    public EntProtoId ActiveCrystalPrototype = "LavalandResurrectionCrystalActive";
}

/// <summary>
///     Active crystal structure that holds a body and revives it after <see cref="ReviveTime"/>.
///     Breaking it before that ejects the body still dead.
/// </summary>
[RegisterComponent]
public sealed partial class ActiveResurrectionCrystalComponent : Component
{
    public const string BodyContainer = "body_slot";

    [DataField]
    public TimeSpan ReviveTime = TimeSpan.FromSeconds(30);

    public TimeSpan ReviveAt;
    public EntityUid? HeldEntity;
}
