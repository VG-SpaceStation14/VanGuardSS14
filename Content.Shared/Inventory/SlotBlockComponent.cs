using Robust.Shared.GameStates;

namespace Content.Shared.Inventory;

/// <summary>
/// Used to prevent items from being unequipped and equipped from slots that are listed in <see cref="Slots"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SlotBlockSystem))]
public sealed partial class SlotBlockComponent : Component
{
    /// <summary>
    /// Slots that this entity should block.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public SlotFlags Slots = SlotFlags.NONE;

    // VG-Tweak Start: VanGuard clothing-order blockers only prevent removal.
    /// <summary>
    /// Whether this blocker also prevents equipping into the covered slots.
    /// </summary>
    [DataField]
    public bool BlockEquip = true;
    // VG-Tweak End
}
