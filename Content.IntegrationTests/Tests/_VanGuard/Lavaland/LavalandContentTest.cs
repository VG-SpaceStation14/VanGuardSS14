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
                var spikeComp = SEntMan.GetComponent<LavalandTendrilComponent>(spike!.Value);
                Assert.That(Server.System<LavalandTendrilSystem>().TryDestroy((spike.Value, spikeComp)), Is.True);
            });
            await Pair.RunTicksSync(2);

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
