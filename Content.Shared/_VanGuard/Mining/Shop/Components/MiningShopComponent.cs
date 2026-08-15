using Content.Shared._VanGuard.Mining.Shop;
using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Mining.Shop.Components;

/// <summary>
/// A vending machine for mining goods. Stores pending orders that can be
/// delivered together with an express delivery.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedMiningShopSystem))]
public sealed partial class MiningShopComponent : Component
{
    /// <summary>
    /// Orders waiting for express delivery.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<MiningShopEntry> OrderList = new();
}
