using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;

namespace Content.Server._VanGuard.Economy.Components;

/// <summary>
///     A wall-mounted ATM. The inserted ID card identifies the account; the
///     account password is checked before the BUI exposes balance or operations.
/// </summary>
[RegisterComponent]
public sealed partial class BankAtmComponent : Component
{
    public const string CardSlotId = "IdCardSlot";

    [DataField]
    public ItemSlot CardSlot = new();
}
