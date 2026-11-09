using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.Carrying;

/// <summary>
/// Per-character state for the Space-key carry mode.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CarryModeComponent : Component
{
    [AutoNetworkedField]
    public bool Enabled;

    [DataField]
    public ProtoId<AlertPrototype> Alert = "CarryMode";
}
