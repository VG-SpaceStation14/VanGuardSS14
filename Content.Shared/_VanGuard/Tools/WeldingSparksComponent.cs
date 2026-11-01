using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.Tools;

/// <summary>
/// Marks a tool as producing a spark effect while a welding do-after is active.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WeldingSparksComponent : Component
{
    [DataField]
    public EntProtoId EffectPrototype = "EffectWeldingSparks";
}
