using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Mining.Shop;

/// <summary>
/// Marks a one-use delivery bag from the mining shop. When used in hand it dumps
/// all contained items onto the floor around the user and deletes itself.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MiningShopDeliveryBagComponent : Component;
