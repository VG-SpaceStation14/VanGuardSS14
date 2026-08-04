using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using System.Numerics;

namespace Content.Shared._VanGuard.Tools;

[RegisterComponent, NetworkedComponent]
public sealed partial class WeldingSparksComponent : Component
{
    [DataField]
    public EntProtoId EffectPrototype = "EffectWeldingSparks";

    public Dictionary<DoAfterId, EntityUid> SpawnedEffects = new();
}