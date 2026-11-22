#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._VanGuard.Lavaland.Components;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.GameTicking.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;
using Content.IntegrationTests.Fixtures;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Lavaland;

/// <summary>
/// Verifies that the Lavaland FTL beacon is published on Lavaland's map and
/// that the Lavaland planet accepts ordinary FTL traffic without a coordinate
/// disk, Jaunter or special admin action.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class LavalandFtlBeaconTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        DummyTicker = false,
        Fresh = true,
        Dirty = true,
    };

    [Test]
    public async Task LavalandFtlBeaconIsVisibleAndUsableAsDestination()
    {
        var ticker = Server.System<ServerGameTicker>();
        Entity<GameRuleComponent>? lavalandRule = null;
        MapId lavalandMap = new();

        try
        {
            await Server.WaitPost(() =>
                lavalandRule = ticker.AddGameRule(new EntProtoId("LavalandLoader")));
            Assert.That(lavalandRule, Is.Not.Null);

            await WaitForLavalandOutpost(lavalandRule!.Value.Owner);

            var ruleComp = SEntMan.GetComponent<LavalandOutpostRuleComponent>(lavalandRule!.Value.Owner);
            Assert.That(ruleComp.MapId, Is.Not.Null);
            lavalandMap = ruleComp.MapId!.Value;

            // (1) Discovery: the beacon must exist on the Lavaland map.
            var beacons = new List<(EntityUid Uid, string Name, MapId Map, Vector2 Pos)>();
            await Server.WaitPost(() =>
            {
                var query = SEntMan.EntityQueryEnumerator<FTLBeaconComponent, MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var meta, out var xform))
                {
                    beacons.Add((uid, meta.EntityName, xform.MapID, xform.WorldPosition));
                }
            });

            var lavalandBeacons = beacons.Where(b => b.Map == lavalandMap).ToList();
            Assert.That(lavalandBeacons, Is.Not.Empty,
                "Lavaland must publish at least one FTLBeacon on its map.");
            Assert.That(lavalandBeacons.Any(b => !string.IsNullOrEmpty(b.Name)), Is.True,
                "At least one Lavaland FTLBeacon must carry a localised name.");

            // (2) Destination: the Lavaland map must register an enabled
            // FTLDestination that does not require a coordinate disk.
            var enabled = false;
            var requiresDisk = true;
            var beaconsOnly = true;
            await Server.WaitPost(() =>
            {
                var query = SEntMan.EntityQueryEnumerator<MapComponent, FTLDestinationComponent>();
                while (query.MoveNext(out var uid, out var mapComp, out var dest))
                {
                    if (mapComp.MapId != lavalandMap)
                        continue;
                    enabled = dest.Enabled;
                    requiresDisk = dest.RequireCoordinateDisk;
                    beaconsOnly = dest.BeaconsOnly;
                }
            });

            Assert.That(enabled, Is.True,
                "The Lavaland FTLDestination must be Enabled for ordinary FTL traffic.");
            Assert.That(requiresDisk, Is.False,
                "Lavaland must be reachable without a coordinate disk.");
            Assert.That(beaconsOnly, Is.False,
                "Lavaland must be reachable as a normal map destination, not as a beacons-only destination.");

            // (3) Real FTL check: CanFTLTo from a fresh shuttle map to Lavaland
            // must succeed. The Lavaland beacon would otherwise never make it
            // onto the station shuttle console.
            var shuttle = await CreateShuttleWithConsole();
            try
            {
                var reachable = false;
                await Server.WaitPost(() =>
                {
                    var shuttleSystem = SEntMan.System<ShuttleSystem>();
                    reachable = shuttleSystem.CanFTLTo(shuttle.Grid, lavalandMap, shuttle.Console);
                });

                Assert.That(reachable, Is.True,
                    "A standard shuttle must be allowed to FTL to Lavaland.");

                // (4) The beacon is actually wired to the same map the shuttle
                // would land on. MapScreen.GetBeacons filters beacons by map, so
                // a misparented beacon would silently disappear from the UI.
                var beaconOnLavaland = false;
                await Server.WaitPost(() =>
                {
                    beaconOnLavaland = lavalandBeacons.Any(b =>
                        SEntMan.EntityExists(b.Uid) &&
                        SEntMan.HasComponent<FTLBeaconComponent>(b.Uid));
                });
                Assert.That(beaconOnLavaland, Is.True,
                    "The Lavaland beacon entity must keep its FTLBeaconComponent.");
            }
            finally
            {
                await DeleteEntity(shuttle.Grid);
            }
        }
        finally
        {
            if (lavalandRule is { } rule && SEntMan.EntityExists(rule.Owner))
            {
                var lavaland = SEntMan.GetComponent<LavalandOutpostRuleComponent>(rule.Owner);
                if (lavaland.Map is { } map && SEntMan.EntityExists(map))
                    await DeleteEntity(map);
                await Server.WaitPost(() =>
                    ticker.EndGameRule((rule.Owner, (GameRuleComponent?) rule.Comp)));
            }
        }
    }

    private record ShuttleFixture(EntityUid Grid, EntityUid Console);

    private async Task<ShuttleFixture> CreateShuttleWithConsole()
    {
        EntityUid grid = default;
        EntityUid console = default;
        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<MapSystem>();
            mapSystem.CreateMap(out var mapId);
            var gridEnt = mapSystem.CreateGridEntity(mapId);
            grid = gridEnt.Owner;
            console = SEntMan.SpawnEntity("ComputerShuttle",
                new EntityCoordinates(grid, Vector2.Zero));
        });

        return new ShuttleFixture(grid, console);
    }

    private async Task<EntityUid> WaitForLavalandOutpost(EntityUid ruleUid)
    {
        for (var i = 0; i < 1800; i++)
        {
            EntityUid? outpost = null;
            await Server.WaitPost(() =>
            {
                var rule = SEntMan.GetComponent<LavalandOutpostRuleComponent>(ruleUid);
                outpost = rule.OutpostGrid;
            });

            if (outpost is { Valid: true })
                return outpost.Value;

            await Pair.RunTicksSync(1);
        }

        Assert.Fail("Timed out waiting for the Lavaland outpost grid to be created.");
        return EntityUid.Invalid;
    }

    private async Task DeleteEntity(EntityUid uid)
    {
        await Server.WaitPost(() =>
        {
            if (SEntMan.EntityExists(uid))
                SEntMan.DeleteEntity(uid);
        });
    }
}
