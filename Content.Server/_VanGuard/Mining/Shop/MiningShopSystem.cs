using System.Linq;
using Content.Shared._VanGuard.Mining.Shop;
using Content.Shared._VanGuard.Mining.Shop.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.GameObjects;

namespace Content.Server._VanGuard.Mining.Shop;

/// <summary>
/// Server-side mining shop logic: refresh the UI after each purchase and deliver all
/// pending orders in a one-use bag when express delivery is requested.
/// </summary>
public sealed partial class MiningShopSystem : SharedMiningShopSystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    protected override void OnVendBui(Entity<MiningShopComponent> vendor, ref MiningShopVendBuiMsg args)
    {
        base.OnVendBui(vendor, ref args);

        _ui.ServerSendUiMessage(vendor.Owner, args.UiKey, new MiningShopRefreshBuiMsg(), args.Actor);
    }

    protected override void OnExpressDeliveryBui(Entity<MiningShopComponent> vendor, ref MiningShopExpressDeliveryBuiMsg args)
    {
        base.OnExpressDeliveryBui(vendor, ref args);

        var actor = args.Actor;
        if (vendor.Comp.OrderList.Count <= 0 || !TryComp(actor, out TransformComponent? xform))
            return;

        var ids = vendor.Comp.OrderList.Select(entry => entry.Id).ToList();
        vendor.Comp.OrderList.Clear();
        Dirty(vendor.Owner, vendor.Comp);

        // Deliver the ordered goods in a one-use bag. It is handed to the buyer and
        // spills its contents on the floor when used.
        var bag = Spawn("MiningShopDeliveryBag", xform.Coordinates);
        foreach (var id in ids)
        {
            var item = Spawn(id, xform.Coordinates);
            _storage.Insert(bag, item, out _);
        }

        // Put the bag directly into the buyer's hands; it stays on the floor if they are full.
        _hands.TryPickupAnyHand(actor, bag);

        _ui.ServerSendUiMessage(vendor.Owner, args.UiKey, new MiningShopRefreshBuiMsg(), args.Actor);
    }

    protected override void OnCancelOrderBui(Entity<MiningShopComponent> vendor, ref MiningShopCancelOrderBuiMsg args)
    {
        base.OnCancelOrderBui(vendor, ref args);

        _ui.ServerSendUiMessage(vendor.Owner, args.UiKey, new MiningShopRefreshBuiMsg(), args.Actor);
    }
}

