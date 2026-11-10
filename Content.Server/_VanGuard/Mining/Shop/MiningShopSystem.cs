using System.Linq;
using Content.Shared._VanGuard.Mining.Shop;
using Content.Shared._VanGuard.Mining.Shop.Components;
using Content.Server._VanGuard.Delivery;
using Robust.Server.GameObjects;

namespace Content.Server._VanGuard.Mining.Shop;

/// <summary>
/// Server-side mining shop logic: refresh the UI after each purchase and deliver all
/// pending orders in a falling delivery pod when express delivery is requested.
/// </summary>
public sealed partial class MiningShopSystem : SharedMiningShopSystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private DeliveryPodSystem _delivery = default!;

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

        // Keep the order list until the pod and all of its contents have been created.
        // A failed delivery must leave the paid goods available for a retry or refund.
        if (!_delivery.TryCreateDelivery(xform.Coordinates, ids, "DeliveryPod"))
        {
            _ui.ServerSendUiMessage(vendor.Owner, args.UiKey, new MiningShopRefreshBuiMsg(), args.Actor);
            return;
        }

        vendor.Comp.OrderList.Clear();
        Dirty(vendor.Owner, vendor.Comp);
        _ui.ServerSendUiMessage(vendor.Owner, args.UiKey, new MiningShopRefreshBuiMsg(), args.Actor);
    }

    protected override void OnCancelOrderBui(Entity<MiningShopComponent> vendor, ref MiningShopCancelOrderBuiMsg args)
    {
        base.OnCancelOrderBui(vendor, ref args);

        _ui.ServerSendUiMessage(vendor.Owner, args.UiKey, new MiningShopRefreshBuiMsg(), args.Actor);
    }
}

