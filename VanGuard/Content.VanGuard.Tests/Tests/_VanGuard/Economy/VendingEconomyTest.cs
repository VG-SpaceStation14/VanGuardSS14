#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._VanGuard.Economy.Components;
using Content.Server.VendingMachines;
using Content.Shared.Mind;
using Content.Shared.VendingMachines;
using Content.Shared.VendingMachines.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Economy;

/// <summary>
///     Verifies that vending machines charge the buyer's bank account when
///     dispensing priced items and that the displayed price comes from the
///     item's estimated sale price (StaticPrice).
/// </summary>
public sealed class VendingEconomyTest : InteractionTest
{
    private const string VendingProtoId = "VendingEconomyTestVending";
    private const string PaidItemProtoId = "VendingEconomyTestPaidItem";
    private const string FreeItemProtoId = "VendingEconomyTestFreeItem";

    [TestPrototypes]
    private const string TestPrototypes = $@"
- type: entity
  parent: BaseItem
  id: {PaidItemProtoId}
  name: economy test paid item
  components:
  - type: StaticPrice
    price: 50

- type: entity
  parent: BaseItem
  id: {FreeItemProtoId}
  name: economy test free item

- type: vendingMachineInventory
  id: VendingEconomyTestInventory
  startingInventory:
    {PaidItemProtoId}: 3
    {FreeItemProtoId}: 2

- type: entity
  id: {VendingProtoId}
  parent: BaseVendingMachine
  components:
  - type: VendingMachine
    pack: VendingEconomyTestInventory
  - type: Sprite
    sprite: Structures/Machines/VendingMachines/cart.rsi
    snapCardinals: true
";

    [Test]
    public async Task EntryPrice_ComesFromStaticPrice()
    {
        var vendingSystem = SEntMan.System<VendingMachineSystem>();
        EntityUid vendor = default;
        await Server.WaitPost(() =>
        {
            vendor = SEntMan.SpawnAtPosition(VendingProtoId, SEntMan.GetCoordinates(TargetCoords));
        });

        // Spawn inventory on map init.
        await RunTicks(2);

        var inventory = vendingSystem.GetAllInventory(vendor);
        Assert.That(inventory, Is.Not.Empty, "vending machine spawned without inventory");

        var paid = inventory.First(x => x.ID == PaidItemProtoId);
        Assert.That(paid.Price, Is.EqualTo(50),
            "the vending price must be taken from the item's StaticPrice");

        var free = inventory.First(x => x.ID == FreeItemProtoId);
        Assert.That(free.Price, Is.EqualTo(25),
            "items without a StaticPrice fall back to the price floor (25)");
    }

    [Test]
    public async Task Purchase_ChargesBankAccount()
    {
        await SpawnTarget(VendingProtoId);
        var vendor = SEntMan.GetEntity(Target.Value);

        // Power the machine so the ejection path is allowed.
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicks(1);

        // Give the player a real mind + bank account and fund it.
        var bank = SEntMan.System<Content.Server._VanGuard.Economy.Systems.EconomyBankSystem>();
        var mindSystem = SEntMan.System<SharedMindSystem>();
        EntityUid mindUid = default;
        await Server.WaitPost(() =>
        {
            var mind = mindSystem.CreateMind(ClientSession.UserId);
            mindUid = mind.Owner;
            mindSystem.SetUserId(mind.Owner, ClientSession.UserId, mind.Comp);
            mindSystem.TransferTo(mind, SPlayer, mind: mind);

            var account = bank.EnsureAccount(mindUid, mind.Comp);
            bank.Deposit((mindUid, account), 500, "test-funding");
        });
        await RunTicks(2);

        // Dispense a priced item through the normal ejection path.
        await Server.WaitPost(() =>
        {
            SEntMan.System<VendingMachineSystem>().AuthorizedVend(
                vendor, SPlayer, InventoryType.Regular, PaidItemProtoId, SEntMan.GetComponent<VendingMachineComponent>(vendor));
        });
        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            var account = SEntMan.GetComponent<EconomyAccountComponent>(mindUid);
            Assert.That(account.Balance, Is.EqualTo(450),
                "buying a 50-credit item must withdraw 50 from the buyer account");
        });
    }
}
