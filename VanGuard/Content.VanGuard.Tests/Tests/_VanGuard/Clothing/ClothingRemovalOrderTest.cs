#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._VanGuard;

[TestFixture]
[NonParallelizable]
public sealed class ClothingRemovalOrderTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    private async Task<(EntityUid Human, MapId Map)> CreateHuman(string prototype = "MobHuman")
    {
        EntityUid? human = null;
        var mapId = MapId.Nullspace;

        await Server.WaitPost(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            maps.CreateMap(out mapId);
            human = SEntMan.SpawnEntity(prototype, new MapCoordinates(Vector2.Zero, mapId));
        });

        Assert.That(human, Is.Not.Null);
        return (human!.Value, mapId);
    }

    private async Task<EntityUid> SpawnOn(EntityUid owner, string prototype)
    {
        EntityUid? entity = null;
        await Server.WaitPost(() =>
        {
            entity = SEntMan.SpawnEntity(prototype, SEntMan.GetComponent<TransformComponent>(owner).Coordinates);
        });
        Assert.That(entity, Is.Not.Null);
        return entity!.Value;
    }

    private async Task<bool> Equip(EntityUid wearer, EntityUid item, string slot)
    {
        var equipped = false;
        await Server.WaitPost(() =>
        {
            var inventory = SEntMan.System<InventorySystem>();
            equipped = inventory.TryEquip(wearer, item, slot);
        });
        return equipped;
    }

    private async Task<bool> Unequip(EntityUid wearer, string slot, bool predicted = false)
    {
        var removed = false;
        await Server.WaitPost(() =>
        {
            var inventory = SEntMan.System<InventorySystem>();
            removed = inventory.TryUnequip(wearer, slot, predicted: predicted);
        });
        return removed;
    }

    private async Task<(bool Allowed, string? Reason)> CanUnequip(EntityUid wearer, string slot)
    {
        var allowed = false;
        string? reason = null;
        await Server.WaitAssertion(() =>
        {
            var inventory = SEntMan.System<InventorySystem>();
            allowed = inventory.CanUnequip(wearer, wearer, slot, out reason);
        });
        return (allowed, reason);
    }

    [Test]
    public async Task BootsCannotBeRemovedUnderHardsuit()
    {
        var (human, _) = await CreateHuman();
        var hardsuit = await SpawnOn(human, "ClothingOuterHardsuitEVA");
        var boots = await SpawnOn(human, "ClothingShoesBootsJack");

        Assert.That(await Equip(human, boots, "shoes"), Is.True);
        Assert.That(await Equip(human, hardsuit, "outerClothing"), Is.True);

        var (allowed, reason) = await CanUnequip(human, "shoes");
        Assert.That(allowed, Is.False, "Boots under a hardsuit must not be removable");
        Assert.That(reason, Is.Not.Null.And.Not.Empty);

        Assert.That(await Unequip(human, "shoes", predicted: true), Is.False,
            "The inventory UI removal path must use the same server check");

        Assert.That(await Unequip(human, "outerClothing"), Is.True);
        Assert.That(await Unequip(human, "shoes"), Is.True, "Boots should be removable after the hardsuit");
    }

    [Test]
    public async Task GlovesCannotBeRemovedUnderHardsuit()
    {
        var (human, _) = await CreateHuman();
        var hardsuit = await SpawnOn(human, "ClothingOuterHardsuitEVA");
        var gloves = await SpawnOn(human, "ClothingHandsGlovesFingerless");

        Assert.That(await Equip(human, gloves, "gloves"), Is.True);
        Assert.That(await Equip(human, hardsuit, "outerClothing"), Is.True);
        Assert.That(await Unequip(human, "gloves"), Is.False);

        Assert.That(await Unequip(human, "outerClothing"), Is.True);
        Assert.That(await Unequip(human, "gloves"), Is.True);
    }

    [Test]
    public async Task JumpsuitCannotBeRemovedUnderOuterClothing()
    {
        var (human, _) = await CreateHuman();
        var jumpsuit = await SpawnOn(human, "ClothingUniformJumpsuitColorGrey");
        var coat = await SpawnOn(human, "ClothingOuterCoatLab");

        Assert.That(await Equip(human, jumpsuit, "jumpsuit"), Is.True);
        Assert.That(await Equip(human, coat, "outerClothing"), Is.True);

        var (allowed, _) = await CanUnequip(human, "jumpsuit");
        Assert.That(allowed, Is.False, "A jumpsuit under outer clothing must not be removable");
        Assert.That(await Unequip(human, "jumpsuit"), Is.False);

        Assert.That(await Unequip(human, "outerClothing"), Is.True);
        Assert.That(await Unequip(human, "jumpsuit"), Is.True);
    }


    [Test]
    public async Task HelmetBlocksRemovalOfEyesAndMask()
    {
        var (human, _) = await CreateHuman();
        var mask = await SpawnOn(human, "ClothingMaskGas");
        var glasses = await SpawnOn(human, "ClothingEyesGlassesSunglasses");
        var helmet = await SpawnOn(human, "ClothingHeadHelmetBasic");

        Assert.That(await Equip(human, mask, "mask"), Is.True);
        Assert.That(await Equip(human, glasses, "eyes"), Is.True);
        Assert.That(await Equip(human, helmet, "head"), Is.True);

        Assert.That(await Unequip(human, "mask"), Is.False);
        Assert.That(await Unequip(human, "eyes"), Is.False);

        Assert.That(await Unequip(human, "head"), Is.True);
        Assert.That(await Unequip(human, "mask"), Is.True);
        Assert.That(await Unequip(human, "eyes"), Is.True);
    }

    [Test]
    public async Task UnrelatedClothingDoesNotBlockRemoval()
    {
        var (human, _) = await CreateHuman();
        var shoes = await SpawnOn(human, "ClothingShoesBootsJack");
        var cloak = await SpawnOn(human, "ClothingNeckCloakCap");

        Assert.That(await Equip(human, shoes, "shoes"), Is.True);
        Assert.That(await Equip(human, cloak, "neck"), Is.True);
        Assert.That(await Unequip(human, "shoes"), Is.True);
    }

    [Test]
    public async Task EquipmentAndReplacementStillWork()
    {
        var (human, _) = await CreateHuman();
        var jumpsuit = await SpawnOn(human, "ClothingUniformJumpsuitColorGrey");
        var coat = await SpawnOn(human, "ClothingOuterCoatLab");
        var hardsuit = await SpawnOn(human, "ClothingOuterHardsuitEVA");

        Assert.That(await Equip(human, jumpsuit, "jumpsuit"), Is.True);
        Assert.That(await Equip(human, coat, "outerClothing"), Is.True);
        Assert.That(await Unequip(human, "outerClothing"), Is.True);
        Assert.That(await Equip(human, hardsuit, "outerClothing"), Is.True, "Normal outer-clothing equipment should still work");
        Assert.That(await Unequip(human, "outerClothing"), Is.True);
    }

    [Test]
    public async Task NonStandardSpeciesUsesTheSameSlotRules()
    {
        var (human, _) = await CreateHuman("MobReptilian");
        var hardsuit = await SpawnOn(human, "ClothingOuterHardsuitEVA");
        var boots = await SpawnOn(human, "ClothingShoesBootsJack");

        Assert.That(await Equip(human, boots, "shoes"), Is.True);
        Assert.That(await Equip(human, hardsuit, "outerClothing"), Is.True);
        Assert.That(await Unequip(human, "shoes"), Is.False);

        Assert.That(await Unequip(human, "outerClothing"), Is.True);
        Assert.That(await Unequip(human, "shoes"), Is.True);
    }
}
