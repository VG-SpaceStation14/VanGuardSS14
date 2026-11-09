using Content.Shared.Alert;
using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.Carrying;

/// <summary>
/// Server-authoritative state of a character carrying another character in their hands.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CarryingComponent : Component
{
    [AutoNetworkedField]
    public EntityUid? Carried;

    [AutoNetworkedField]
    public EntityUid? Anchor;

    [DataField]
    public float WalkSpeedModifier = 0.6f;

    [DataField]
    public float SprintSpeedModifier = 0.7f;

    [DataField]
    public ProtoId<AlertPrototype> Alert = "CarryDrop";

    public bool Ending;

    /// <summary>
    /// Entry animation state. The temporary anchor starts at the position the
    /// target occupied before the pickup and moves into the carrier's hands.
    /// </summary>
    public Vector2 AnchorStartLocal;
    public TimeSpan TransitionStart;
    public TimeSpan TransitionEnd;
}
