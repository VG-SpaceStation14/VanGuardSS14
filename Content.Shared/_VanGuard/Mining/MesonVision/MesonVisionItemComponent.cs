using Content.Shared.Inventory;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.Mining.MesonVision;

/// <summary>
/// Added to an item (e.g. meson goggles) so that wearing it in the configured slot grants
/// meson vision to the wearer, toggleable via an action.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedMesonVisionSystem))]
public sealed partial class MesonVisionItemComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId ActionId = "ActionToggleMesonVision";

    [DataField, AutoNetworkedField]
    public EntityUid? Action;

    [AutoNetworkedField]
    public EntityUid? User;

    [DataField, AutoNetworkedField]
    public bool Toggleable = true;

    /// <summary>
    /// The inventory slot the item must occupy to grant vision.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SlotFlags SlotFlags { get; set; } = SlotFlags.EYES;
}
