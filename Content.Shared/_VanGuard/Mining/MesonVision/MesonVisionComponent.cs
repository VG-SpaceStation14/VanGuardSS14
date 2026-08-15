using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Mining.MesonVision;

/// <summary>
/// Toggleable "meson vision" on an entity (usually a player): grants an overlay that renders
/// walls and doors through everything while active. Added either innately or by worn goggles.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedMesonVisionSystem))]
public sealed partial class MesonVisionComponent : Component
{
    /// <summary>
    /// Optional alert shown while the vision is active, allowing toggling via the alert.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype>? Alert;

    [DataField, AutoNetworkedField]
    public MesonVisionState State = MesonVisionState.Full;

    [DataField, AutoNetworkedField]
    public bool Overlay;

    /// <summary>
    /// Whether this vision is innate (does not get removed when goggles are unequipped).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Innate;

    [DataField, AutoNetworkedField]
    public Color Color = Color.FromHex("#D3D3D3");
}

[Serializable, NetSerializable]
public enum MesonVisionState
{
    Off,
    Full
}

public sealed partial class ToggleMesonVisionAlertEvent : BaseAlertEvent;
