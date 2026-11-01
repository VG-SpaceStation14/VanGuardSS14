using System.Numerics;
using System.Linq;
using Content.Server._VanGuard.Research.Components;
using Content.Server._VanGuard.Research.Systems;
using Content.Server.Station.Systems;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard;

[TestFixture]
public sealed class ResearchScannerLifecycleTest : GameTest
{
    [Test]
    public async Task SelectAndAbandonOrderUpdatesStationAndScanner()
    {
        var map = await Pair.CreateTestMap();
        var coords = new EntityCoordinates(map.Grid, Vector2.Zero);
        ProtoId<GameMapPrototype> mapId = "MeteorArena";
        var mapProto = SProtoMan.Index(mapId);

        EntityUid scanner = default;
        EntityUid station = default;
        await Server.WaitPost(() =>
        {
            station = SEntMan.System<ServerStationSystem>().InitializeNewStation(
                mapProto.Stations["Arena"],
                new[] { map.Grid.Owner },
                "Research Test",
                mapProto);

            scanner = SEntMan.SpawnAtPosition("ExperimentScanner", coords);
            SEntMan.System<ServerStationSystem>().AddGridToStation(station, map.Grid);
        });

        await Server.WaitAssertion(() =>
        {
            var scannerSystem = SEntMan.System<ExperimentScannerSystem>();
            Assert.That(scannerSystem.TryGetStationDatabase(scanner, out var foundStation, out var stationDb), Is.True);
            Assert.That(foundStation, Is.EqualTo(station));

            scannerSystem.FillAvailableOrders(station, "Experiments", 7, stationDb);
            Assert.That(stationDb.AvailableOrders, Is.Not.Empty);

            var scannerDb = SEntMan.GetComponent<ExperimentScannerDatabaseComponent>(scanner);
            var orderId = stationDb.AvailableOrders[0].Id;
            scannerSystem.SelectOrder(scanner, orderId, null, "Experiments", 7);

            Assert.That(scannerDb.ActiveOrder, Is.Not.Null);
            Assert.That(scannerDb.ActiveOrder!.Id, Is.EqualTo(orderId));
            Assert.That(stationDb.AvailableOrders.Any(order => order.Id == orderId), Is.False);

            scannerSystem.AbandonOrder(scanner, null, "Experiments", 7);
            Assert.That(scannerDb.ActiveOrder, Is.Null);
            Assert.That(stationDb.AvailableOrders.Any(order => order.Id == orderId), Is.True);
        });
    }
}
