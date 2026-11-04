using System.Numerics;
#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._VanGuard.Lavaland;
using Content.Shared.Interaction;
using Content.Shared.Actions;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Content.Shared.Chasm;
using Robust.Shared.Prototypes;
using Content.Shared.Pinpointer;
using Content.Shared._VanGuard.Mining.Shop;
using Content.Shared._VanGuard.Mining.Shop.Components;
using Content.Shared._VanGuard.Mining.Points;
using Content.Shared._VanGuard.Mining.Points.Components;
using Content.Shared.Audio;
using Robust.Shared.Audio;
using Content.Shared.Weather;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Station.Components;
using Content.Shared.Station.Systems;
using Content.Shared.Construction.Prototypes;
using Content.Server.Construction.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Maths;

using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;


using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Shared.GameObjects;

using Robust.Shared.Map.Components;


namespace Content.IntegrationTests.Tests._VanGuard.Lavaland;

[TestFixture]
public sealed class LavalandContentTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    public static PoolSettings Fresh => new()
    {
        Connected = true,
        Dirty = true,
        Fresh = true,
        DummyTicker = false,
    };

    [Test]
    public void LavalandContentPrototypesExist()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("LavalandTendril")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("LavalandNecropolisCrate")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("Jaunter")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("JaunterPortal")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("ClothingOuterArmorMiner")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("ClothingOuterArmorMinerReinforcedFull")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("LavalandTendrilIcewing")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("ClothingUniformJumpsuitMinerExplorer")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("ClothingShoesFootWrapsWhite")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<RoleLoadoutPrototype>("JobMiner")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<LoadoutGroupPrototype>("MinerBelt")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<LoadoutGroupPrototype>("MinerOuter")), Is.True);

            var head = SProtoMan.Index<LoadoutGroupPrototype>(new ProtoId<LoadoutGroupPrototype>("MinerHead"));
            var neck = SProtoMan.Index<LoadoutGroupPrototype>(new ProtoId<LoadoutGroupPrototype>("MinerNeck"));
            var outer = SProtoMan.Index<LoadoutGroupPrototype>(new ProtoId<LoadoutGroupPrototype>("MinerOuter"));
            Assert.That(head.Loadouts.Any(id => id.Id == "MinerWinterHood"), Is.False);
            Assert.That(neck.Loadouts.Any(id => id.Id is "MinerGoliathCloak" or "MinerSupremeCloak"), Is.False);
            Assert.That(outer.Loadouts.Any(id => id.Id == "MinerArmor"), Is.False);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("ClothingHeadHelmetMinerReinforcedFull")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<ConstructionPrototype>("ClothingOuterArmorMinerReinforcedFull")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<ConstructionGraphPrototype>("MinerArmorReinforcedFull")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<AmbientMusicPrototype>("Lavaland")), Is.True);

            Assert.That(SProtoMan.HasIndex(new ProtoId<SoundCollectionPrototype>("LavalandAmbience")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("LavalandMobBloodMiner")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("LavalandMobBloodMinerBuffed")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("LavalandSpawnMobBloodMiner")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("LavalandCleavingSaw")), Is.True);
            Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("ActionBloodMinerShoot")), Is.True);
            foreach (var id in new[]
                     {
                         "MobAncientGoliath",
                         "LavalandMobLegion",
                         "LavalandMobLegionSkull",
                         "LavalandTendrilAncientGoliath",
                         "LavalandTendrilLegion",
                         "LavalandLegionCore",
                         "LavalandLegionCoreDecayed",
                         "LavalandLegionCoreStable",
                         "LavalandStabilizingSerum",
                         "LavalandWeaponStaffHealing",
                         "LavalandRareMobSpawner",
                     })
            {
                Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>(id)), Is.True,
                    $"Missing Lavaland prototype {id}.");
            }
            Assert.That(SProtoMan.HasIndex(new ProtoId<ConstructionGraphPrototype>("LavalandLegionCoreStable")), Is.True);
        });
    }

    [Test]
    public async Task TendrilDropsNecropolisCache()
    {
        EntityUid? spike = null;
        var grid = GetStationGrid();
        var player = Pair.Player!.AttachedEntity!.Value;

        try
        {
            await Server.WaitPost(() =>
            {
                spike = SEntMan.SpawnEntity("LavalandTendril", new EntityCoordinates(grid, 1000, 1000));
            });
            await Pair.RunTicksSync(2);
            await Server.WaitPost(() =>
            {
                var damage = new DamageSpecifier(SProtoMan.Index<DamageTypePrototype>(new ProtoId<DamageTypePrototype>("Heat")), 300);
                Assert.That(Server.System<DamageableSystem>().TryChangeDamage(spike!.Value, damage, ignoreResistances: true, ignoreGlobalModifiers: true), Is.True);
            });
            await Pair.RunTicksSync(4);

            var lootCount = 0;
            await Server.WaitPost(() =>
            {
                var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
                while (query.MoveNext(out _, out var meta))
                {
                    if (meta.EntityPrototype?.ID == "LavalandNecropolisCrate")
                        lootCount++;
                }
            });

            Assert.That(lootCount, Is.EqualTo(1));
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (spike is { } spikeUid && SEntMan.EntityExists(spikeUid))
                    SEntMan.DeleteEntity(spikeUid);
            });
        }
    }

    [Test]
    public async Task LegionDropsCorpseAndCoreOnDeath()
    {
        var grid = GetStationGrid();
        EntityUid legion = default;

        try
        {
            await Server.WaitPost(() =>
            {
                legion = SEntMan.SpawnEntity("LavalandMobLegion", new EntityCoordinates(grid, 0, 0));
                var damage = new DamageSpecifier(
                    SProtoMan.Index<DamageTypePrototype>(new ProtoId<DamageTypePrototype>("Slash")), 100);
                Assert.That(Server.System<DamageableSystem>()
                    .TryChangeDamage(legion, damage, ignoreResistances: true, ignoreGlobalModifiers: true), Is.True);
            });
            await Pair.RunTicksSync(4);

            await Server.WaitPost(() =>
            {
                var corpseCount = 0;
                var coreCount = 0;
                var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
                while (query.MoveNext(out _, out var meta))
                {
                    if (meta.EntityPrototype?.ID == "MobRandomCargoCorpse")
                        corpseCount++;
                    if (meta.EntityPrototype?.ID == "LavalandLegionCore")
                        coreCount++;
                }

                Assert.That(corpseCount, Is.EqualTo(1), "Legion did not leave a humanoid corpse.");
                Assert.That(coreCount, Is.EqualTo(1), "Legion did not drop a core.");
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (legion != default && SEntMan.EntityExists(legion))
                    SEntMan.DeleteEntity(legion);
            });
        }
    }

    [Test]
    public async Task HealingStaffRechargesOverTime()
    {
        var grid = GetStationGrid();
        EntityUid staff = default;

        try
        {
            await Server.WaitPost(() =>
            {
                staff = SEntMan.SpawnEntity("LavalandWeaponStaffHealing", new EntityCoordinates(grid, 0, 0));
                Assert.That(SEntMan.TryGetComponent(staff, out BasicEntityAmmoProviderComponent? ammo), Is.True);
                Assert.That(ammo!.Capacity, Is.EqualTo(10));
                Assert.That(SEntMan.TryGetComponent(staff, out RechargeBasicEntityAmmoComponent? recharge), Is.True);
                Assert.That(recharge!.RechargeCooldown, Is.EqualTo(10f));
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (staff != default && SEntMan.EntityExists(staff))
                    SEntMan.DeleteEntity(staff);
            });
        }
    }

    [Test]
    public async Task LegionCoreHealsAndIsConsumed()
    {
        var grid = GetStationGrid();
        EntityUid patient = default;
        EntityUid core = default;

        try
        {
            await Server.WaitPost(() =>
            {
                patient = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 3000, 3000));
                var damage = new DamageSpecifier(
                    SProtoMan.Index<DamageTypePrototype>(new ProtoId<DamageTypePrototype>("Blunt")), 50);
                Assert.That(Server.System<DamageableSystem>()
                    .TryChangeDamage(patient, damage, ignoreResistances: true, ignoreGlobalModifiers: true), Is.True);

                core = SEntMan.SpawnEntity("LavalandLegionCore", SEntMan.GetComponent<TransformComponent>(patient).Coordinates);
                Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(patient, core), Is.True,
                    "Could not put the legion core into the patient's hand.");
                var use = new UseInHandEvent(patient);
                SEntMan.EventBus.RaiseLocalEvent(core, use);
                Assert.That(use.Handled, Is.True, "Using the legion core was not handled.");
            });

            await Pair.RunTicksSync(30);

            await Server.WaitPost(() =>
            {
                Assert.That(SEntMan.EntityExists(core), Is.False,
                    "The legion core was not consumed after use.");
                Assert.That(Server.System<DamageableSystem>().GetTotalDamage(patient).Float(), Is.LessThan(50f),
                    "The legion core did not heal the patient.");
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (core != default && SEntMan.EntityExists(core))
                    SEntMan.DeleteEntity(core);
                if (patient != default && SEntMan.EntityExists(patient))
                    SEntMan.DeleteEntity(patient);
            });
        }
    }

    [Test]
    public async Task CleavingSawHasTwoCombatModes()
    {
        var grid = GetStationGrid();
        var saw = default(EntityUid);
        try
        {
            await Server.WaitPost(() =>
            {
                saw = SEntMan.SpawnEntity("LavalandCleavingSaw", new EntityCoordinates(grid, 1200, 1200));
                var melee = SEntMan.GetComponent<MeleeWeaponComponent>(saw);
                Assert.That(melee.Damage.GetTotal().Float(), Is.EqualTo(12f),
                    "The saw must start in its inactive, low-damage mode.");

                var toggle = SEntMan.GetComponent<ItemToggleComponent>(saw);
                Assert.That(toggle.Activated, Is.False);
                Server.System<ItemToggleSystem>().Toggle((saw, toggle));
                Assert.That(SEntMan.GetComponent<ItemToggleComponent>(saw).Activated, Is.True);
                Assert.That(SEntMan.GetComponent<MeleeWeaponComponent>(saw).Damage.GetTotal().Float(),
                    Is.EqualTo(30f), "The active saw must use its high-damage mode.");
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (SEntMan.EntityExists(saw))
                    SEntMan.DeleteEntity(saw);
            });
        }
    }

    [Test]
    public async Task FailedShelterDeploymentStartsCooldown()
    {
        var grid = GetStationGrid();
        var player = Pair.Player!.AttachedEntity!.Value;
        var capsule = default(EntityUid);
        var wall = default(EntityUid);

        try
        {
            await Server.WaitPost(() =>
            {
                var coords = new EntityCoordinates(grid, 1400, 1400);
                wall = SEntMan.SpawnEntity("WallSolid", coords);
                capsule = SEntMan.SpawnEntity("ShelterCapsule", coords);
                SEntMan.EventBus.RaiseLocalEvent(capsule, new UseInHandEvent(player));
            });

            await Pair.RunTicksSync(170);

            await Server.WaitPost(() =>
            {
                var comp = SEntMan.GetComponent<ShelterCapsuleComponent>(capsule);
                Assert.That(comp.CooldownUntil, Is.Not.Null,
                    "A failed deployment must start the failure cooldown.");
                Assert.That(comp.CooldownUntil!.Value, Is.GreaterThan(Server.ResolveDependency<IGameTiming>().CurTime),
                    "The failure cooldown must still be active after the failed deployment.");

                SEntMan.EventBus.RaiseLocalEvent(capsule, new UseInHandEvent(player));
                Assert.That(comp.NextSpawn, Is.Null,
                    "Using the capsule during the failure cooldown must not begin another deployment.");
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (SEntMan.EntityExists(capsule))
                    SEntMan.DeleteEntity(capsule);
                if (SEntMan.EntityExists(wall))
                    SEntMan.DeleteEntity(wall);
            });
        }
    }

    [Test]
    public async Task AllShelterCapsulesCompleteCycle()
    {
        var grid = GetStationGrid();
        var player = Pair.Player!.AttachedEntity!.Value;
        var capsules = new List<EntityUid>();
        var baseline = 0;
        var mapId = default(MapId);

        await Server.WaitPost(() =>
        {
            mapId = SEntMan.GetComponent<TransformComponent>(grid).MapID;
            var query = SEntMan.EntityQueryEnumerator<MapGridComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                if (SEntMan.GetComponent<TransformComponent>(uid).MapID == mapId)
                    baseline++;
            }

            var protos = new[] { "ShelterCapsule", "ShelterCapsuleLuxury", "ShelterCapsuleBar" };
            for (var i = 0; i < protos.Length; i++)
            {
                var capsule = SEntMan.SpawnEntity(protos[i], new EntityCoordinates(grid, 2000 + i * 100, 2000));
                capsules.Add(capsule);
                SEntMan.EventBus.RaiseLocalEvent(capsule, new UseInHandEvent(player));
            }
        });

        await Pair.RunTicksSync(700);

        await Server.WaitPost(() =>
        {
            foreach (var capsule in capsules)
                Assert.That(SEntMan.EntityExists(capsule), Is.False, "Capsule was not consumed after deployment.");

            var after = 0;
            var hidden = 0;
            var query = SEntMan.EntityQueryEnumerator<MapGridComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                if (SEntMan.GetComponent<TransformComponent>(uid).MapID != mapId)
                    continue;
                after++;
                if (Server.System<SharedShuttleSystem>().HasIFFFlag(uid, IFFFlags.Hide))
                    hidden++;
            }
            Assert.That(after, Is.GreaterThanOrEqualTo(baseline + 3), "Not all shelter interiors were loaded.");
            Assert.That(hidden, Is.GreaterThanOrEqualTo(3), "Shelter grids were not hidden from mass scanner.");
        });
    }

    [Test]
    public async Task MiningShopSellsJaunterAndCapsules()
    {
        var grid = GetStationGrid();
        var player = Pair.Player!.AttachedEntity!.Value;
        EntityUid shop = default;

        await Server.WaitPost(() =>
        {
            var points = Server.System<MiningPointsSystem>();
            var card = points.TryFindIdCard(player);
            if (card is null)
                Assert.Fail("Test player has no ID card with mining points.");
            Assert.That(points.AddPoints(card!.Value, 20000), Is.True);

            shop = SEntMan.SpawnEntity("MiningShop", new EntityCoordinates(grid, 5, 0));
            var motion = SProtoMan.Index<MiningShopSectionPrototype>(new ProtoId<MiningShopSectionPrototype>("Motion"));
            var entries = motion.Entries.Where(entry =>
                entry.Id == "Jaunter" || entry.Id == "ShelterCapsule" ||
                entry.Id == "ShelterCapsuleLuxury" || entry.Id == "ShelterCapsuleBar").ToList();
            Assert.That(entries.Count, Is.EqualTo(4), "MiningShop must contain Jaunter and all three capsules.");
            Assert.That(entries.Any(entry => entry.Id == "ShelterCapsuleLivingRoom"), Is.False);

            var medicine = SProtoMan.Index<MiningShopSectionPrototype>(new ProtoId<MiningShopSectionPrototype>("Medicine"));
            var serum = medicine.Entries.SingleOrDefault(entry => entry.Id == "LavalandStabilizingSerum");
            Assert.That(serum, Is.Not.Null, "MiningShop Medicine must sell the stabilizing serum.");
            Assert.That(serum!.Price, Is.EqualTo(300u));

            uint total = 0;
            foreach (var entry in entries)
            {
                total += entry.Price ?? 0;
                var msg = new MiningShopVendBuiMsg(entry)
                {
                    Actor = player,
                    UiKey = MiningShopUI.Key,
                };
                SEntMan.EventBus.RaiseLocalEvent(shop, msg);
            }

            var after = points.TryFindIdCard(player);
            Assert.That(after, Is.Not.Null);
            Assert.That(after!.Value.Comp!.Points, Is.EqualTo(20000 - total));
            Assert.That(SEntMan.GetComponent<MiningShopComponent>(shop).OrderList.Count, Is.EqualTo(4));

            var express = new MiningShopExpressDeliveryBuiMsg
            {
                Actor = player,
                UiKey = MiningShopUI.Key,
            };
            SEntMan.EventBus.RaiseLocalEvent(shop, express);
            var bagQuery = SEntMan.EntityQueryEnumerator<MiningShopDeliveryBagComponent>();
            Assert.That(bagQuery.MoveNext(out _, out _), Is.True, "Express delivery did not produce a delivery bag.");
        });
    }

    [Test]
    public async Task MinerArmorHasConstructionAndHelmetKit()
    {
        var grid = GetStationGrid();
        await Server.WaitPost(() =>
        {
            var armor = SEntMan.SpawnEntity("ClothingOuterArmorMinerReinforcedFull", new EntityCoordinates(grid, 0, 0));
            var helmet = SEntMan.SpawnEntity("ClothingHeadHelmetMinerReinforcedFull", new EntityCoordinates(grid, 0, 0));
            try
            {
                Assert.That(SEntMan.HasComponent<ConstructionComponent>(armor), Is.True);
                Assert.That(SEntMan.HasComponent<ToggleableClothingComponent>(armor), Is.True);
                Assert.That(SEntMan.GetComponent<ToggleableClothingComponent>(armor).ClothingPrototype,
                    Is.EqualTo("ClothingHeadHelmetMinerReinforcedFull"));
                Assert.That(SEntMan.HasComponent<ClothingComponent>(helmet), Is.True);
            }
            finally
            {
                SEntMan.DeleteEntity(armor);
                SEntMan.DeleteEntity(helmet);
            }
        });
    }

    [Test]
    public async Task MinerContainersHaveExpectedEquipment()
    {
        var grid = GetStationGrid();
        var containers = Server.System<SharedContainerSystem>();
        EntityUid locker = default;
        EntityUid suit = default;

        await Server.WaitPost(() =>
        {
            locker = SEntMan.SpawnEntity("LockerMinerFilled", new EntityCoordinates(grid, 0, 0));
            suit = SEntMan.SpawnEntity("SuitStorageMiner", new EntityCoordinates(grid, 0, 0));
        });
        await Pair.RunTicksSync(4);

        await Server.WaitPost(() =>
        {
            Assert.That(containers.TryGetContainer(locker, "entity_storage", out var lockerContainer), Is.True);
            Assert.That(containers.TryGetContainer(suit, "entity_storage", out var suitContainer), Is.True);

            var lockerIds = lockerContainer!.ContainedEntities
                .Select(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID).ToList();
            var suitIds = suitContainer!.ContainedEntities
                .Select(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID).ToList();
            Assert.That(lockerIds, Does.Contain("ClothingEyesGlassesMesonScanner"));
            Assert.That(suitIds, Does.Contain("ClothingOuterArmorMiner"));
            Assert.That(suitIds, Does.Not.Contain("ClothingOuterHardsuitMining"));
        });
    }

    [Test]
    public async Task MinerLoadoutEquipsJumpsuitAndShoes()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human", Sex.Male);
            var loadout = new RoleLoadout(new ProtoId<RoleLoadoutPrototype>("JobMiner"));
            loadout.SetDefault(profile, ServerSession, SProtoMan, force: true);
            profile.SetLoadout(loadout);

            var mob = Server.System<StationSpawningSystem>().SpawnPlayerMob(
                new EntityCoordinates(map.Grid, 0, 0),
                "Miner",
                profile,
                null);
            var inventory = Server.System<InventorySystem>();
            Assert.That(inventory.TryGetSlotEntity(mob, "jumpsuit", out var jumpsuit), Is.True);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(jumpsuit!.Value).EntityPrototype?.ID,
                Is.EqualTo("ClothingUniformJumpsuitMinerExplorer"));
            Assert.That(inventory.TryGetSlotEntity(mob, "shoes", out var shoes), Is.True);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(shoes!.Value).EntityPrototype?.ID,
                Does.StartWith("ClothingShoes"));
        });
    }

    [Test]
    public async Task MinerArmorTogglesItsHelmet()
    {
        var grid = GetStationGrid();
        EntityUid player = default;
        await Server.WaitPost(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human", Sex.Male);
            player = Server.System<StationSpawningSystem>().SpawnPlayerMob(
                new EntityCoordinates(grid, 0, 0), "Miner", profile, null);
        });
        await Pair.RunTicksSync(2);

        var inventory = Server.System<InventorySystem>();
        var actions = Server.System<SharedActionsSystem>();
        var containers = Server.System<SharedContainerSystem>();
        var gasTanks = Server.System<GasTankSystem>();
        var armors = new[]
        {
            ("ClothingOuterArmorMiner", "ClothingHeadHelmetMiner"),
            ("ClothingOuterArmorMinerReinforcedOne", "ClothingHeadHelmetMinerReinforcedOne"),
            ("ClothingOuterArmorMinerReinforcedTwo", "ClothingHeadHelmetMinerReinforcedTwo"),
            ("ClothingOuterArmorMinerReinforcedFull", "ClothingHeadHelmetMinerReinforcedFull"),
        };

        try
        {
            foreach (var (armorId, helmetId) in armors)
            {
                EntityUid armor = default;
                EntityUid helmet = default;
                EntityUid? tank = null;
                EntityUid? mask = null;

                await Server.WaitPost(() =>
                {
                    armor = SEntMan.SpawnEntity(armorId, new EntityCoordinates(grid, 0, 0));
                });
                await Pair.RunTicksSync(2);

                await Server.WaitPost(() =>
                {
                    var toggle = SEntMan.GetComponent<ToggleableClothingComponent>(armor);
                    Assert.That(toggle.ClothingUid, Is.Not.Null);
                    Assert.That(toggle.ActionEntity, Is.Not.Null, "Armor did not create a toggle action.");
                    Assert.That(toggle.Action?.Id, Is.EqualTo("ActionToggleSuitPiece"));
                    helmet = toggle.ClothingUid!.Value;
                    Assert.That(SEntMan.GetComponent<MetaDataComponent>(helmet).EntityPrototype?.ID,
                        Is.EqualTo(helmetId));
                    Assert.That(containers.TryGetContainer(armor, "toggleable-clothing", out var container), Is.True);
                    Assert.That(container!.ContainedEntities.Contains(helmet), Is.True,
                        "The helmet is not stored inside the armor.");

                    Assert.That(inventory.TryEquip(player, armor, "outerClothing"), Is.True,
                        $"Could not equip {armorId} through the real inventory path.");
                });
                await Pair.RunTicksSync(3);

                await Server.WaitPost(() =>
                {
                    var toggle = SEntMan.GetComponent<ToggleableClothingComponent>(armor);
                    var action = actions.GetActions(player)
                        .FirstOrDefault(act => act.Owner == toggle.ActionEntity!.Value);
                    Assert.That(action.Owner, Is.Not.EqualTo(EntityUid.Invalid),
                        "The helmet toggle action was not granted to the wearer.");
                    Assert.That(inventory.TryGetSlotEntity(player, "head", out _), Is.False);

                    // Toggle the helmet on through the granted action, not by
                    // raising the event directly.
                    actions.PerformAction(player, action);
                });
                await Pair.RunTicksSync(3);

                await Server.WaitPost(() =>
                {
                    Assert.That(inventory.TryGetSlotEntity(player, "head", out var head), Is.True,
                        "The helmet action did not put the helmet on the wearer.");
                    Assert.That(head!.Value, Is.EqualTo(helmet));
                    actions.PerformAction(player, actions.GetActions(player)
                        .First(act => act.Owner == SEntMan.GetComponent<ToggleableClothingComponent>(armor).ActionEntity!.Value));
                });
                await Pair.RunTicksSync(3);

                await Server.WaitPost(() =>
                {
                    Assert.That(inventory.TryGetSlotEntity(player, "head", out _), Is.False);
                    Assert.That(containers.TryGetContainer(armor, "toggleable-clothing", out var container), Is.True);
                    Assert.That(container!.ContainedEntities.Contains(helmet), Is.True,
                        "Toggling the helmet off did not return it to the armor.");

                    // An occupied head slot must block the toggle instead of
                    // deleting or dropping either item.
                    var hat = SEntMan.SpawnEntity("ClothingHeadHatBunny", new EntityCoordinates(grid, 0, 0));
                    Assert.That(inventory.TryEquip(player, hat, "head"), Is.True);
                    actions.PerformAction(player, actions.GetActions(player)
                        .First(act => act.Owner == SEntMan.GetComponent<ToggleableClothingComponent>(armor).ActionEntity!.Value));
                    Assert.That(inventory.TryGetSlotEntity(player, "head", out var wornHat), Is.True);
                    Assert.That(wornHat!.Value, Is.EqualTo(hat), "The toggle replaced an existing hat.");
                    Assert.That(container.ContainedEntities.Contains(helmet), Is.True);
                    SEntMan.DeleteEntity(hat);

                    actions.PerformAction(player, actions.GetActions(player)
                        .First(act => act.Owner == SEntMan.GetComponent<ToggleableClothingComponent>(armor).ActionEntity!.Value));
                });
                await Pair.RunTicksSync(3);

                await Server.WaitPost(() =>
                {
                    // A gas tank must fit through the real whitelist, not with
                    // force: true, and internals must be able to use it.
                    tank = SEntMan.SpawnEntity("OxygenTankFilled", new EntityCoordinates(grid, 0, 0));
                    mask = SEntMan.SpawnEntity("ClothingMaskGasExplorer", new EntityCoordinates(grid, 0, 0));
                    Assert.That(inventory.TryEquip(player, tank.Value, "suitstorage"), Is.True,
                        $"Armor {armorId} rejected a gas tank in suit storage.");
                    Assert.That(inventory.TryEquip(player, mask.Value, "mask"), Is.True);
                });
                await Pair.RunTicksSync(3);

                await Server.WaitPost(() =>
                {
                    var tankComp = SEntMan.GetComponent<GasTankComponent>(tank!.Value);
                    Assert.That(gasTanks.CanConnectToInternals((tank.Value, tankComp)), Is.True,
                        "The suit-storage tank cannot be used for internals.");
                    Assert.That(inventory.TryGetSlotEntity(player, "head", out var head), Is.True);
                    Assert.That(head!.Value, Is.EqualTo(helmet));

                    SEntMan.DeleteEntity(mask!.Value);
                    SEntMan.DeleteEntity(tank.Value);
                    mask = null;
                    tank = null;

                    Assert.That(inventory.TryUnequip(player, "outerClothing", out var removedArmor), Is.True);
                    Assert.That(removedArmor!.Value, Is.EqualTo(armor));
                    Assert.That(inventory.TryGetSlotEntity(player, "head", out _), Is.False,
                        "Unequipping the armor left the helmet on the wearer.");
                    Assert.That(containers.TryGetContainer(armor, "toggleable-clothing", out var container), Is.True);
                    Assert.That(container!.ContainedEntities.Contains(helmet), Is.True,
                        "Unequipping the armor did not return the helmet to it.");
                });
                await Pair.RunTicksSync(2);

                await Server.WaitPost(() =>
                {
                    SEntMan.DeleteEntity(armor);
                });
            }
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                foreach (var slot in new[] { "outerClothing", "head", "suitstorage", "mask" })
                {
                    if (inventory.TryGetSlotEntity(player, slot, out var worn))
                        SEntMan.DeleteEntity(worn.Value);
                }
            });
        }
    }

    [Test]
    public async Task NecropolisCratesGenerateDistinctLootOnce()
    {
        var grid = GetStationGrid();
        var containers = Server.System<SharedContainerSystem>();
        var crates = new[]
        {
            ("LavalandNecropolisCrate", "SteelOre"),
            ("LavalandNecropolisCrateGoliath", "MaterialGoliathHide1"),
            ("LavalandNecropolisCrateWatcher", "PlasmaOre"),
        };
        var spawned = new List<(EntityUid Uid, int Count)>();

        await Server.WaitPost(() =>
        {
            foreach (var (prototype, _) in crates)
                spawned.Add((SEntMan.SpawnEntity(prototype, new EntityCoordinates(grid, 0, 0)), 0));
        });
        await Pair.RunTicksSync(4);

        await Server.WaitPost(() =>
        {
            for (var i = 0; i < crates.Length; i++)
            {
                var (crate, required) = crates[i];
                Assert.That(containers.TryGetContainer(spawned[i].Uid, "entity_storage", out var container), Is.True);
                var ids = container!.ContainedEntities
                    .Select(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID)
                    .Where(id => id != null)
                    .ToList();
                Assert.That(ids, Is.Not.Empty, $"{crate} generated no loot.");
                Assert.That(ids, Does.Contain(required), $"{crate} is missing its guaranteed category item.");
                Assert.That(ids.All(id => SProtoMan.HasIndex<EntityPrototype>(id!)), Is.True);
                spawned[i] = (spawned[i].Uid, ids.Count);
            }
        });

        await Pair.RunTicksSync(20);
        await Server.WaitPost(() =>
        {
            foreach (var (uid, before) in spawned)
            {
                Assert.That(containers.TryGetContainer(uid, "entity_storage", out var container), Is.True);
                Assert.That(container!.ContainedEntities.Count, Is.EqualTo(before),
                    "A Necropolis crate generated loot more than once.");
            }
        });
    }

    [Test]
    public async Task TendrilVariantsUseMatchingNecropolisCrate()
    {
        var grid = GetStationGrid();
        await Server.WaitPost(() =>
        {
            foreach (var (tendrilProto, crateProto) in new[]
            {
                ("LavalandTendril", "LavalandNecropolisCrate"),
                ("LavalandTendrilGoliath", "LavalandNecropolisCrateGoliath"),
                ("LavalandTendrilWatcher", "LavalandNecropolisCrateWatcher"),
            })
            {
                var tendril = SEntMan.SpawnEntity(tendrilProto, new EntityCoordinates(grid, 0, 0));
                try
                {
                    Assert.That(SEntMan.GetComponent<LavalandTendrilComponent>(tendril).Loot, Is.EqualTo(crateProto));
                }
                finally
                {
                    SEntMan.DeleteEntity(tendril);
                }
            }
        });
    }

    [Test]
    [PairConfig(nameof(Fresh))]
    public async Task LavalandFaunaIsStormImmuneButPlayersAndAnimalsAreNot()
    {
        var grid = GetStationGrid();
        var mapId = SEntMan.GetComponent<TransformComponent>(grid).MapID;
        var entities = new List<EntityUid>();
        var faunae = new[]
        {
            "MobWatcherLavaland",
            "MobWatcherIcewing",
            "MobWatcherMagmawing",
            "MobWatcherPride",
            "MobGoliath",
            "MobHivelord",
            "MobBasilisk",
        };

        try
        {
            await Server.WaitPost(() => Server.System<SharedWeatherSystem>().TrySetWeather(mapId, null, out _));
            await Server.WaitPost(() =>
            {
                foreach (var proto in faunae)
                    entities.Add(SEntMan.SpawnEntity(proto, new EntityCoordinates(grid, 10, 0)));
                entities.Add(SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 20, 0)));
                entities.Add(SEntMan.SpawnEntity("MobChicken", new EntityCoordinates(grid, 30, 0)));
                Assert.That(Server.System<SharedWeatherSystem>().TrySetWeather(mapId, "WeatherAshfall", out _), Is.True);
            });
            await Pair.RunTicksSync(10);

            await Server.WaitPost(() =>
            {
                var damage = Server.System<DamageableSystem>();
                for (var i = 0; i < faunae.Length; i++)
                    Assert.That(damage.GetTotalDamage(entities[i]).Float(), Is.EqualTo(0f), $"{faunae[i]} took storm damage.");
                Assert.That(damage.GetTotalDamage(entities[^2]).Float(), Is.GreaterThan(0f), "Player took no storm damage.");
                Assert.That(damage.GetTotalDamage(entities[^1]).Float(), Is.GreaterThan(0f), "Ordinary animal took no storm damage.");

                Assert.That(damage.TryChangeDamage(entities[0],
                    new DamageSpecifier(SProtoMan.Index<DamageTypePrototype>(new ProtoId<DamageTypePrototype>("Heat")), 10),
                    ignoreResistances: true), Is.True);
                Assert.That(damage.GetTotalDamage(entities[0]).Float(), Is.GreaterThan(0f));
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.System<SharedWeatherSystem>().TrySetWeather(mapId, null, out _);
                foreach (var entity in entities)
                {
                    if (SEntMan.EntityExists(entity))
                        SEntMan.DeleteEntity(entity);
                }
            });
        }
    }

    [Test]
    public async Task TendrilMarkersRejectLavaAndChasmTiles()
    {
        var grid = GetStationGrid();
        var spawned = new List<EntityUid>();
        var center = new Vector2i(1000, 1000);

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);
            var tileMan = Server.ResolveDependency<ITileDefinitionManager>();
            var basalt = new Tile(tileMan["FloorBasalt"].TileId);

            // A real floor patch, then a ring of lava and chasms covering every
            // tile a feature marker could pick within its two-tile radius.
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                    mapSystem.SetTile(grid, gridComp, center + new Vector2i(x, y), basalt);
            }

            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                {
                    var coords = mapSystem.GridTileToLocal(grid, gridComp, center + new Vector2i(x, y));
                    var proto = (x + y) % 2 == 0 ? "FloorLavaEntity" : "FloorChasmEntity";
                    spawned.Add(SEntMan.SpawnEntity(proto, coords));
                }
            }

            spawned.Add(SEntMan.SpawnEntity("LavalandTendrilFeature",
                mapSystem.GridTileToLocal(grid, gridComp, center)));
        });

        await Pair.RunTicksSync(6);

        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out var meta))
            {
                if (meta.EntityPrototype?.ID.StartsWith("LavalandTendril") != true)
                    continue;

                Assert.Fail($"A tendril ({meta.EntityPrototype.ID}) was created on a lava/chasm tile.");
            }

            foreach (var entity in spawned)
                if (SEntMan.EntityExists(entity))
                    SEntMan.DeleteEntity(entity);
        });
    }

    private EntityUid GetStationGrid()
    {
        var query = SEntMan.EntityQueryEnumerator<StationDataComponent>();
        var stationSystem = Server.System<Content.Server.Station.Systems.ServerStationSystem>();
        while (query.MoveNext(out var station, out var data))
        {
            if (stationSystem.GetLargestGrid((station, data)) is { } grid)
                return grid;
        }

        throw new InvalidOperationException("Test station has no grid.");
    }
}
