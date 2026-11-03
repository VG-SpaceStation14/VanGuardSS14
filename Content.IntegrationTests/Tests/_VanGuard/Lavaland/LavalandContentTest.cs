#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._VanGuard.Lavaland;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Content.Shared.Chasm;
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
using Robust.Shared.Containers;
using Robust.Shared.Map;

using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;


using Content.IntegrationTests.Fixtures;
using Robust.Shared.GameObjects;

using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Lavaland;

[TestFixture]
public sealed class LavalandContentTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        DummyTicker = false,
        Fresh = true,
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
    public async Task JaunterPortalAndFallWork()
    {
        var grid = GetStationGrid();
        var player = Pair.Player!.AttachedEntity!.Value;
        EntityUid beacon = default;
        EntityUid chasm = default;

        try
        {
            await Server.WaitPost(() =>
            {
                beacon = SEntMan.SpawnEntity("WarpPointBeacon", new EntityCoordinates(grid, 60, 60));
                var jaunter = SEntMan.SpawnEntity("Jaunter", new EntityCoordinates(grid, 0, 0));
                Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(player, jaunter), Is.True);
                SEntMan.EventBus.RaiseLocalEvent(jaunter, new UseInHandEvent(player));
            });
            await Pair.RunTicksSync(10);

            await Server.WaitPost(() =>
            {
                var portalQuery = SEntMan.EntityQueryEnumerator<PortalComponent>();
                var found = false;
                while (portalQuery.MoveNext(out var portalUid, out _))
                {
                    if (Server.System<LinkedEntitySystem>().GetLink(portalUid, out var target) && target == beacon)
                        found = true;
                }
                Assert.That(found, Is.True, "Jaunter portal was not linked to the station beacon.");
            });

            await Server.WaitPost(() =>
            {
                var jaunter = SEntMan.SpawnEntity("Jaunter", new EntityCoordinates(grid, 0, 0));
                Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(player, jaunter), Is.True);
                chasm = SEntMan.SpawnEntity("FloorChasmEntity", SEntMan.GetComponent<TransformComponent>(player).Coordinates);
                var ev = new EntityStartFallingAttemptEvent(player);
                SEntMan.EventBus.RaiseLocalEvent(chasm, ref ev);
                Assert.That(ev.Cancelled, Is.True, "Jaunter did not cancel the chasm fall.");
            });
            await Pair.RunTicksSync(4);
            await Server.WaitPost(() =>
            {
                var playerXform = SEntMan.GetComponent<TransformComponent>(player);
                var beaconXform = SEntMan.GetComponent<TransformComponent>(beacon);
                Assert.That((playerXform.WorldPosition - beaconXform.WorldPosition).Length(), Is.LessThan(2f));
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (SEntMan.EntityExists(beacon)) SEntMan.DeleteEntity(beacon);
                if (SEntMan.EntityExists(chasm)) SEntMan.DeleteEntity(chasm);
            });
        }
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
        var armors = new[]
        {
            ("ClothingOuterArmorMinerReinforcedOne", "ClothingHeadHelmetMinerReinforcedOne"),
            ("ClothingOuterArmorMinerReinforcedTwo", "ClothingHeadHelmetMinerReinforcedTwo"),
            ("ClothingOuterArmorMinerReinforcedFull", "ClothingHeadHelmetMinerReinforcedFull"),
        };

        try
        {
            foreach (var (armorId, helmetId) in armors)
            {
                EntityUid armor = default;
                await Server.WaitPost(() =>
                {
                    armor = SEntMan.SpawnEntity(armorId, new EntityCoordinates(grid, 0, 0));
                });
                await Pair.RunTicksSync(2);
                await Server.WaitPost(() =>
                {
                    var toggle = SEntMan.GetComponent<ToggleableClothingComponent>(armor);
                    Assert.That(toggle.ClothingUid, Is.Not.Null);
                    Assert.That(SEntMan.GetComponent<MetaDataComponent>(toggle.ClothingUid!.Value).EntityPrototype?.ID,
                        Is.EqualTo(helmetId));
                    Assert.That(inventory.TryGetSlotContainer(player, "outerClothing", out var slot, out _), Is.True);
                    Assert.That(Server.System<SharedContainerSystem>().Insert(armor, slot!), Is.True);
                    SEntMan.EventBus.RaiseLocalEvent(armor, new ToggleClothingEvent { Performer = player });
                });
                await Pair.RunTicksSync(2);
                await Server.WaitPost(() =>
                {
                    Assert.That(inventory.TryGetSlotEntity(player, "head", out var head), Is.True);
                    Assert.That(SEntMan.GetComponent<MetaDataComponent>(head!.Value).EntityPrototype?.ID, Is.EqualTo(helmetId));
                    SEntMan.EventBus.RaiseLocalEvent(armor, new ToggleClothingEvent { Performer = player });
                });
                await Pair.RunTicksSync(2);
                await Server.WaitPost(() =>
                {
                    Assert.That(inventory.TryGetSlotEntity(player, "head", out _), Is.False);
                    SEntMan.DeleteEntity(armor);
                });
            }
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (inventory.TryGetSlotEntity(player, "outerClothing", out var worn))
                    SEntMan.DeleteEntity(worn.Value);
                if (inventory.TryGetSlotEntity(player, "head", out var head))
                    SEntMan.DeleteEntity(head.Value);
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

            // Immunity must not block ordinary damage sources.
            Assert.That(damage.TryChangeDamage(entities[0],
                new DamageSpecifier(SProtoMan.Index<DamageTypePrototype>(new ProtoId<DamageTypePrototype>("Heat")), 10),
                ignoreResistances: true), Is.True);
            Assert.That(damage.GetTotalDamage(entities[0]).Float(), Is.GreaterThan(0f));
            Server.System<SharedWeatherSystem>().TrySetWeather(mapId, null, out _);
            foreach (var entity in entities)
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
