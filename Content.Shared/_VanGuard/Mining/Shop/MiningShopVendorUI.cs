using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Mining.Shop;

[Serializable, NetSerializable]
public enum MiningShopUI : byte
{
    Key
}

/// <summary>
/// Sent to the server to add an entry to the order list (deducting points from the buyer's ID card).
/// </summary>
[Serializable, NetSerializable]
public sealed class MiningShopVendBuiMsg(MiningShopEntry entry) : BoundUserInterfaceMessage
{
    public readonly MiningShopEntry Entry = entry;
}

/// <summary>
/// Sent to the server to deliver all pending orders at once.
/// </summary>
[Serializable, NetSerializable]
public sealed class MiningShopExpressDeliveryBuiMsg : BoundUserInterfaceMessage;

/// <summary>
/// Sent to the server to cancel (and refund) one pending order by its index.
/// </summary>
[Serializable, NetSerializable]
public sealed class MiningShopCancelOrderBuiMsg(int index) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
}

/// <summary>
/// Sent from the server to refresh the shop UI.
/// </summary>
[Serializable, NetSerializable]
public sealed class MiningShopRefreshBuiMsg : BoundUserInterfaceMessage;
