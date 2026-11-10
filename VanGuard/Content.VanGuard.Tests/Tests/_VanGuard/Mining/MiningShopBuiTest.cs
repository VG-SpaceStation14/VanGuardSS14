#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._VanGuard.Mining.Shop;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._VanGuard.Delivery.Components;
using Content.Shared._VanGuard.Mining.Shop;
using Content.Shared._VanGuard.Mining.Shop.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Mining;

/// <summary>
/// Verifies that the mining shop vendor machine opens its bound user interface on activation.
/// </summary>
[TestFixture]
public sealed class MiningShopBuiTest : InteractionTest
{
    [Test]
    public async Task OpenMiningShopBui()
    {
        await SpawnTarget("MiningShop");

        // The machine needs grid power before it opens its UI on activation.
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicksSync(1);

        await Activate();
        Assert.That(IsUiOpen(MiningShopUI.Key), "mining shop BUI failed to open.");
    }

    [Test]
    public async Task OpenMiningShopBuiWithoutPower()
    {
        await SpawnTarget("MiningShop");
        await RunTicksSync(1);

        await Activate();
        Assert.That(IsUiOpen(MiningShopUI.Key), "mining shop BUI should open even without grid power.");
    }

    [Test]
    public async Task MiningShopWindowPopulatesSections()
    {
        await SpawnTarget("MiningShop");
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicksSync(1);

        await Activate();
        Assert.That(IsUiOpen(MiningShopUI.Key), "mining shop BUI failed to open.");

        await RunTicksSync(5);

        // Diagnostic: how many mining shop section prototypes does the client see?
        var protoMan = Client.ResolveDependency<IPrototypeManager>();
        List<MiningShopSectionPrototype>? clientSections = null;
        await Client.WaitPost(() =>
        {
            clientSections = protoMan.EnumeratePrototypes<MiningShopSectionPrototype>().ToList();
        });
        Assert.That(clientSections, Is.Not.Null, "client prototype enumeration returned null.");
        Assert.That(clientSections!.Count, Is.GreaterThan(0),
            $"client sees {clientSections.Count} mining shop sections.");

        var window = GetWindow<MiningShopWindow>();
        Assert.That(window, Is.Not.Null, "mining shop window was not created.");
        Assert.That(window.Catalog.ChildCount, Is.GreaterThan(0), "mining shop window has no category blocks in the catalog.");
    }

    [Test]
    public async Task ExpressDeliveryCreatesPodWithAllOrderedGoods()
    {
        await SpawnTarget("MiningShop");
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicksSync(1);

        await Activate();
        Assert.That(IsUiOpen(MiningShopUI.Key), "mining shop BUI failed to open.");
        await RunTicksSync(3);

        await Server.WaitPost(() =>
        {
            var shopSys = SEntMan.System<SharedMiningShopSystem>();
            var vendor = SEntMan.GetEntity(Target)!.Value;
            shopSys.AddOrder(vendor, new MiningShopEntry { Id = "Flare" });
            shopSys.AddOrder(vendor, new MiningShopEntry { Id = "Pickaxe" });
        });

        await SendBui(MiningShopUI.Key, new MiningShopExpressDeliveryBuiMsg());
        await RunTicks(5);

        EntityUid pod = default;
        var contentIds = new List<string>();
        var ordersLeft = -1;
        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<DeliveryPodComponent>();
            Assert.That(query.MoveNext(out var uid, out var delivery), Is.True,
                "express delivery did not create a delivery pod");
            pod = uid;
            contentIds = delivery!.Contents.ContainedEntities
                .Select(e => SEntMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID ?? string.Empty)
                .ToList();
            ordersLeft = SEntMan.GetComponent<MiningShopComponent>(SEntMan.GetEntity(Target)!.Value).OrderList.Count;
        });

        Assert.That(ordersLeft, Is.EqualTo(0), "successful delivery must clear the order list once");
        Assert.That(contentIds, Is.EquivalentTo(new[] { "Flare", "Pickaxe" }),
            "the pod must contain every ordered item exactly once");

        await RunTicks(180);

        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.EntityExists(pod), Is.False,
                "the falling pod must be replaced by the opened pod after landing");
            var opened = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
            var foundOpened = false;
            while (opened.MoveNext(out _, out var meta))
            {
                if (meta.EntityPrototype?.ID == "DeliveryPodOpened")
                {
                    foundOpened = true;
                    break;
                }
            }
            Assert.That(foundOpened, Is.True, "the opened pod/crater entity must exist after landing");
        });

        var flares = 0;
        var pickaxes = 0;
        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out _, out var meta))
            {
                switch (meta.EntityPrototype?.ID)
                {
                    case "Flare":
                        flares++;
                        break;
                    case "Pickaxe":
                        pickaxes++;
                        break;
                }
            }
        });
        Assert.That(flares, Is.EqualTo(1), "the flare must be delivered exactly once");
        Assert.That(pickaxes, Is.EqualTo(1), "the pickaxe must be delivered exactly once");

        await RunTicks(30);
        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
            var afterFlares = 0;
            var afterPickaxes = 0;
            while (query.MoveNext(out _, out var meta))
            {
                if (meta.EntityPrototype?.ID == "Flare")
                    afterFlares++;
                else if (meta.EntityPrototype?.ID == "Pickaxe")
                    afterPickaxes++;
            }

            Assert.That(afterFlares, Is.EqualTo(1), "landing must not be processed twice for the flare");
            Assert.That(afterPickaxes, Is.EqualTo(1), "landing must not be processed twice for the pickaxe");
        });
    }

    [Test]
    public async Task CancelOrderRefundsPointsAndRemovesOrder()
    {
        await SpawnTarget("MiningShop");
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicksSync(1);

        await Activate();
        Assert.That(IsUiOpen(MiningShopUI.Key), "mining shop BUI failed to open.");
        await RunTicksSync(3);

        // Give the player an ID card with mining points so the refund has somewhere to go.
        await Server.WaitPost(() =>
        {
            var card = SEntMan.SpawnEntity("PassengerIDCard", SEntMan.GetCoordinates(PlayerCoords));
            SEntMan.System<Content.Shared._VanGuard.Mining.Points.MiningPointsSystem>().AddPoints(card, 1000);
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(SPlayer, card);
        });

        // Place an order with a price (simulating a real purchase).
        await Server.WaitPost(() =>
        {
            var shopSys = SEntMan.System<SharedMiningShopSystem>();
            var vendor = SEntMan.GetEntity(Target)!.Value;
            shopSys.AddOrder(vendor, new Content.Shared._VanGuard.Mining.Shop.MiningShopEntry { Id = "Flare", Price = 75 });
        });

        // Cancel it through the client BUI.
        await SendBui(MiningShopUI.Key, new MiningShopCancelOrderBuiMsg(0));
        await RunTicks(3);

        // The order should be gone and the price refunded to the card.
        Content.Shared._VanGuard.Mining.Shop.Components.MiningShopComponent comp = default!;
        uint cardPoints = 0;
        await Server.WaitPost(() =>
        {
            var vendor = SEntMan.GetEntity(Target)!.Value;
            comp = SEntMan.GetComponent<Content.Shared._VanGuard.Mining.Shop.Components.MiningShopComponent>(vendor);
            var card = SEntMan.System<SharedHandsSystem>().EnumerateHeld(SPlayer)
                .FirstOrDefault(e => SEntMan.HasComponent<Content.Shared._VanGuard.Mining.Points.Components.MiningPointsComponent>(e));
            cardPoints = SEntMan.GetComponent<Content.Shared._VanGuard.Mining.Points.Components.MiningPointsComponent>(card).Points;
        });

        Assert.That(comp.OrderList, Is.Empty, "cancel should remove the order from the list.");
        Assert.That(cardPoints, Is.EqualTo(1075), "cancel should refund the order price to the ID card.");
    }

    [Test]
    public async Task ExpressDeliveryPodFitsHardsuit()
    {
        await SpawnTarget("MiningShop");
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicksSync(1);

        await Activate();
        Assert.That(IsUiOpen(MiningShopUI.Key), "mining shop BUI failed to open.");
        await RunTicksSync(3);

        await Server.WaitPost(() =>
        {
            var shopSys = SEntMan.System<SharedMiningShopSystem>();
            var vendor = SEntMan.GetEntity(Target)!.Value;
            shopSys.AddOrder(vendor, new MiningShopEntry { Id = "ClothingOuterHardsuitSalvage" });
        });

        await SendBui(MiningShopUI.Key, new MiningShopExpressDeliveryBuiMsg());
        await RunTicks(5);

        var containsHardsuit = false;
        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<DeliveryPodComponent>();
            Assert.That(query.MoveNext(out _, out var delivery), Is.True,
                "express delivery did not create a delivery pod");
            containsHardsuit = delivery!.Contents.ContainedEntities.Any(e =>
                SEntMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "ClothingOuterHardsuitSalvage");
        });
        Assert.That(containsHardsuit, Is.True, "the hardsuit should fit inside the delivery pod.");
    }

    [Test]
    public async Task ExpressDeliveryFailureKeepsPaidOrders()
    {
        await SpawnTarget("MiningShop");
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicksSync(1);
        await Activate();
        await RunTicksSync(3);

        await Server.WaitPost(() =>
        {
            SEntMan.System<SharedMiningShopSystem>().AddOrder(
                SEntMan.GetEntity(Target)!.Value,
                new MiningShopEntry { Id = "ThisPrototypeDoesNotExist", Price = 100 });
        });

        await SendBui(MiningShopUI.Key, new MiningShopExpressDeliveryBuiMsg());
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var vendor = SEntMan.GetEntity(Target)!.Value;
            Assert.That(SEntMan.GetComponent<MiningShopComponent>(vendor).OrderList, Has.Count.EqualTo(1),
                "a failed pod creation must keep the paid order available for retry/refund");
            var pod = SEntMan.EntityQueryEnumerator<DeliveryPodComponent>();
            Assert.That(pod.MoveNext(out _, out _), Is.False, "failed creation must not leave a pod");
        });
    }

}
