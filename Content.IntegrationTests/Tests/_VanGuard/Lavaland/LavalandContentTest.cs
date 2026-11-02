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
using Robust.Shared.Map;

using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;
using Content.Shared.Station.Components;
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
            Assert.That(points.AddPoints(card.Value, 20000), Is.True);

            shop = SEntMan.SpawnEntity("MiningShop", new EntityCoordinates(grid, 5, 0));
            var motion = SProtoMan.Index<MiningShopSectionPrototype>("Motion");
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
            Assert.That(after!.Value.Comp.Points, Is.EqualTo(20000 - total));
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
