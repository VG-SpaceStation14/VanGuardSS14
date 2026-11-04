#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._VanGuard.Mining.Shop;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._VanGuard.Mining.Shop;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Storage;
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
    public async Task ExpressDeliveryHandsBagAndSpillsOnUse()
    {
        await SpawnTarget("MiningShop");
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicksSync(1);

        await Activate();
        Assert.That(IsUiOpen(MiningShopUI.Key), "mining shop BUI failed to open.");
        await RunTicksSync(3);

        // Simulate two purchases by adding them to the vendor's order list.
        await Server.WaitPost(() =>
        {
            var shopSys = SEntMan.System<SharedMiningShopSystem>();
            var vendor = SEntMan.GetEntity(Target)!.Value;
            shopSys.AddOrder(vendor, new Content.Shared._VanGuard.Mining.Shop.MiningShopEntry { Id = "Flare" });
            shopSys.AddOrder(vendor, new Content.Shared._VanGuard.Mining.Shop.MiningShopEntry { Id = "Pickaxe" });
        });

        // Request express delivery through the open client BUI.
        await SendBui(MiningShopUI.Key, new MiningShopExpressDeliveryBuiMsg());

        // The delivery bag should now be in the player's hands.
        EntityUid held = default;
        await Server.WaitPost(() =>
        {
            held = SEntMan.System<SharedHandsSystem>().EnumerateHeld(SPlayer).FirstOrDefault();
        });
        Assert.That(held != default, Is.True, "delivery bag should be in the player's hands.");
        Assert.That(SEntMan.HasComponent<MiningShopDeliveryBagComponent>(held),
            "held entity should be the delivery bag.");

        // Use the bag in hand: the ordered items spill onto the floor and the bag is deleted.
        await UseInHand();
        await RunTicks(3);

        await AssertEntityLookup(
            new EntitySpecifierCollection(new List<EntitySpecifier>
            {
                new("Flare", 1),
                new("Pickaxe", 1)
            }),
            failOnMissing: true,
            failOnExcess: false);

        Assert.That(SEntMan.Deleted(held) || !SEntMan.HasComponent<MiningShopDeliveryBagComponent>(held),
            "delivery bag should be deleted after use.");
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
    public async Task ExpressDeliveryBagFitsHardsuit()
    {
        await SpawnTarget("MiningShop");
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicksSync(1);

        await Activate();
        Assert.That(IsUiOpen(MiningShopUI.Key), "mining shop BUI failed to open.");
        await RunTicksSync(3);

        // Order a hardsuit (item size Ginormous) - it must fit into the delivery bag.
        await Server.WaitPost(() =>
        {
            var shopSys = SEntMan.System<SharedMiningShopSystem>();
            var vendor = SEntMan.GetEntity(Target)!.Value;
            shopSys.AddOrder(vendor, new Content.Shared._VanGuard.Mining.Shop.MiningShopEntry { Id = "ClothingOuterHardsuitSalvage" });
        });

        await SendBui(MiningShopUI.Key, new MiningShopExpressDeliveryBuiMsg());
        await RunTicks(3);

        EntityUid bag = default;
        await Server.WaitPost(() =>
        {
            bag = SEntMan.System<SharedHandsSystem>().EnumerateHeld(SPlayer)
                .FirstOrDefault(e => SEntMan.HasComponent<MiningShopDeliveryBagComponent>(e));
        });
        Assert.That(bag != default, Is.True, "delivery bag should be in the player's hands.");

        bool containsHardsuit = false;
        await Server.WaitPost(() =>
        {
            var storage = SEntMan.GetComponent<StorageComponent>(bag);
            containsHardsuit = storage.Container.ContainedEntities.Any(e =>
                SEntMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "ClothingOuterHardsuitSalvage");
        });
        Assert.That(containsHardsuit, Is.True, "the hardsuit should fit inside the delivery bag.");
    }
}
