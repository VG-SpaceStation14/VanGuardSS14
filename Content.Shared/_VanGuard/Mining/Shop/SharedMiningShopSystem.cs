using Content.Shared._VanGuard.Mining.Points;
using Content.Shared._VanGuard.Mining.Shop.Components;
using Robust.Shared.Network;

namespace Content.Shared._VanGuard.Mining.Shop;

/// <summary>
/// Shared logic for the mining shop vendor: validating purchases against the buyer's
/// mining points and tracking the order list.
/// </summary>
public abstract partial class SharedMiningShopSystem : EntitySystem
{
    [Dependency] private MiningPointsSystem _miningPoints = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<MiningShopComponent>(MiningShopUI.Key, subs =>
        {
            subs.Event<MiningShopVendBuiMsg>(OnVendBui);
            subs.Event<MiningShopExpressDeliveryBuiMsg>(OnExpressDeliveryBui);
            subs.Event<MiningShopCancelOrderBuiMsg>(OnCancelOrderBui);
        });
    }

    protected virtual void OnVendBui(Entity<MiningShopComponent> vendor, ref MiningShopVendBuiMsg args)
    {
        // Try to charge the buyer's ID card before placing the order.
        if (args.Entry.Price is { } price)
        {
            var actor = args.Actor;
            if (_miningPoints.TryFindIdCard(actor) is not { } idCard ||
                !_miningPoints.RemovePoints(idCard, price))
            {
                return;
            }

            if (_net.IsServer)
                Dirty(vendor);
        }

        if (_net.IsClient)
            return;

        vendor.Comp.OrderList.Add(args.Entry);
        Dirty(vendor);
    }

    protected virtual void OnExpressDeliveryBui(Entity<MiningShopComponent> vendor, ref MiningShopExpressDeliveryBuiMsg args)
    {
        // Server-side delivery handled in the server system.
    }

    /// <summary>
    /// Cancels one pending order: refunds its price to the buyer's ID card and removes it.
    /// </summary>
    protected virtual void OnCancelOrderBui(Entity<MiningShopComponent> vendor, ref MiningShopCancelOrderBuiMsg args)
    {
        if (_net.IsClient)
            return;

        var index = args.Index;
        if (index < 0 || index >= vendor.Comp.OrderList.Count)
            return;

        var entry = vendor.Comp.OrderList[index];

        // Refund the price that was charged when the order was placed.
        if (entry.Price is { } price && _miningPoints.TryFindIdCard(args.Actor) is { } idCard)
            _miningPoints.AddPoints(idCard, price);

        vendor.Comp.OrderList.RemoveAt(index);
        Dirty(vendor);
    }

    /// <summary>
    /// Adds an order to the vendor's order list. Used by tests and other systems that
    /// want to place orders programmatically.
    /// </summary>
    public void AddOrder(EntityUid vendor, MiningShopEntry entry)
    {
        if (!TryComp<MiningShopComponent>(vendor, out var comp))
            return;

        comp.OrderList.Add(entry);
        Dirty(vendor, comp);
    }
}

