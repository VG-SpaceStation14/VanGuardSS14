using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._VanGuard.Lavaland;
using Content.Server._VanGuard.Shuttles.Components;
using Content.Server._VanGuard.Shuttles.Systems;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.GameTicking.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._VanGuard.Shuttles;

[TestFixture]
[NonParallelizable]
public sealed class MiningShuttleTest : GameTest
{
    private const string ShuttlePath = "/Maps/_VanGuard/Shuttles/NT - Mining";
    private const string OutpostPath = "/Maps/_VanGuard/Lavaland/mining_outpost.yml";

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        DummyTicker = false,
    };

    [Test]
    public async Task MapsUseTheVanGuardPrototypeIds()
    {
        var stationGrid = GetStationGrid();
        var mapId = Xform(stationGrid).MapID;

        var shuttle = await LoadGrid(mapId, ShuttlePath);
        try
        {
            var ids = await EntityPrototypeIds(shuttle);
            Assert.That(ids, Does.Not.Contain("AirlockExternalShuttleLocked"));
            Assert.That(ids, Does.Not.Contain("ComputerShuttle"));
            Assert.That(ids, Contains.Item("AirlockShuttleMiningLocked"));
            Assert.That(ids, Contains.Item("MiningShuttleConsole"));
        }
        finally
        {
            await DeleteEntity(shuttle);
        }

        var outpost = await LoadGrid(mapId, OutpostPath);
        try
        {
            var ids = await EntityPrototypeIds(outpost);
            Assert.That(ids, Does.Not.Contain("AirlockExternalShuttleLocked"));
            Assert.That(ids, Contains.Item("AirlockShuttleMiningLocked"));

            // The prepared outpost copy contains a stock ComputerShuttle. It is not
            // part of the shuttle-side replacement and is intentionally reported.
            Assert.That(ids, Does.Contain("ComputerShuttle"));
        }
        finally
        {
            await DeleteEntity(outpost);
        }
    }

    [Test]
    public async Task ConsoleListsTaggedDocksAndDocks()
    {
        var stationGrid = GetStationGrid();
        var mapId = Xform(stationGrid).MapID;
        var shuttle = await LoadGrid(mapId, ShuttlePath);
        var targetDock = await SpawnMiningDock(stationGrid, new Vector2(50, 0));
        EntityUid? card = null;

        try
        {
            await Server.WaitPost(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(SEntMan.HasComponent<DockingComponent>(targetDock), Is.True, "dock has no Docking");
                    Assert.That(SEntMan.HasComponent<PriorityDockComponent>(targetDock), Is.True, "dock has no PriorityDock");
                    Assert.That(SEntMan.GetComponent<PriorityDockComponent>(targetDock).Tag?.Id,
                        Is.EqualTo("DockMining"), "dock tag mismatch");
                    Assert.That(Xform(targetDock).GridUid, Is.EqualTo(stationGrid), "dock is not on the station grid");
                    Assert.That(Xform(targetDock).Anchored, Is.True, "dock is not anchored");
                });
            });

            var console = await FindConsole(shuttle);
            var consoleSystem = SEntMan.System<MiningShuttleConsoleSystem>();

            List<MiningShuttleDockTarget>? targets = null;
            await Server.WaitPost(() => targets = consoleSystem.GetDockTargets(console));
            Assert.That(targets!.Any(target => target.Dock == targetDock), Is.True,
                "The tagged station dock was not offered by the console.");
            var target = targets.Single(target => target.Dock == targetDock);
            Assert.That(target.Enabled, Is.True,
                $"The free station dock should be selectable. reason={target.Reason ?? "<none>"}");

            var player = Pair.Player!.AttachedEntity!.Value;
            var denied = false;
            string? deniedReason = null;
            await Server.WaitPost(() => denied = consoleSystem.TryTravel(console, targetDock, player, out deniedReason));
            Assert.That(denied, Is.False);
            Assert.That(deniedReason, Is.Not.Null, "Travel without access should be rejected.");

            await Server.WaitPost(() =>
            {
                card = SEntMan.SpawnEntity("PassengerIDCard", Xform(player).Coordinates);
                SEntMan.EnsureComponent<AccessComponent>(card.Value).Tags.Add("Salvage");
                Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(player, card.Value), Is.True);
            });

            var travelled = false;
            string? error = null;
            await Server.WaitPost(() => travelled = consoleSystem.TryTravel(console, targetDock, player, out error));
            Assert.That(travelled, Is.True, error);

            bool docked = false;
            await Server.WaitPost(() => docked = SEntMan.GetComponent<DockingComponent>(targetDock).Docked);
            Assert.That(docked, Is.True, "The shuttle did not dock with the selected dock.");

            var occupied = true;
            string? occupiedReason = null;
            await Server.WaitPost(() => occupied = consoleSystem.TryTravel(console, targetDock, player, out occupiedReason));
            Assert.That(occupied, Is.False);
            Assert.That(occupiedReason, Is.Not.Null, "A busy dock should be rejected without throwing.");

            var missing = true;
            string? missingReason = null;
            await Server.WaitPost(() => missing = consoleSystem.TryTravel(console, EntityUid.Invalid, player, out missingReason));
            Assert.That(missing, Is.False);
            Assert.That(missingReason, Is.Not.Null, "A missing dock should be rejected without throwing.");
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (card is { } cardUid && SEntMan.EntityExists(cardUid))
                    SEntMan.DeleteEntity(cardUid);

                if (SEntMan.EntityExists(targetDock))
                    SEntMan.DeleteEntity(targetDock);

                if (SEntMan.EntityExists(shuttle))
                    SEntMan.DeleteEntity(shuttle);
            });
        }
    }

    [Test]
    public async Task SpawnRuleDocksToStationDock()
    {
        var stationGrid = GetStationGrid();
        var stationMap = Xform(stationGrid).MapID;
        var targetDock = await SpawnMiningDock(stationGrid, new Vector2(50, 0));
        Entity<GameRuleComponent>? rule = null;

        try
        {
            var ticker = Server.System<ServerGameTicker>();
            var loader = new EntProtoId("MiningShuttleLoader");
            await Server.WaitPost(() => rule = ticker.AddGameRule(loader));

            Assert.That(rule, Is.Not.Null);
            var spawnRule = SEntMan.GetComponent<MiningShuttleSpawnRuleComponent>(rule!.Value.Owner);
            Assert.That(spawnRule.ShuttleGrid, Is.Not.Null);

            var shuttle = spawnRule.ShuttleGrid!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(Xform(shuttle).MapID, Is.EqualTo(stationMap));
                Assert.That(spawnRule.DockedToFallback, Is.False);
                Assert.That(SEntMan.GetComponent<DockingComponent>(targetDock).Docked, Is.True);
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (rule is { } r && SEntMan.EntityExists(r.Owner))
                {
                    var spawnRule = SEntMan.GetComponent<MiningShuttleSpawnRuleComponent>(r.Owner);
                    if (spawnRule.ShuttleGrid is { } shuttle && SEntMan.EntityExists(shuttle))
                        SEntMan.DeleteEntity(shuttle);
                    Server.System<ServerGameTicker>().EndGameRule(r);
                }

                if (SEntMan.EntityExists(targetDock))
                    SEntMan.DeleteEntity(targetDock);
            });
        }
    }

    [Test]
    public async Task RoundstartPresetSpawnsMiningShuttle()
    {
        var ticker = Server.System<ServerGameTicker>();

        try
        {
            await Pair.WaitCommand("golobby");
            await Pair.WaitClientCommand("toggleready True");
            await Pair.WaitCommand("setgamepreset Extended 9999");
            await Pair.WaitCommand("startround");
            await Pair.RunUntilSynced();

            Entity<MiningShuttleSpawnRuleComponent>? spawnRule = null;
            Entity<LavalandOutpostRuleComponent>? lavalandRule = null;
            await Server.WaitPost(() =>
            {
                var spawnRules = SEntMan.EntityQueryEnumerator<MiningShuttleSpawnRuleComponent>();
                while (spawnRules.MoveNext(out var uid, out var spawn))
                {
                    spawnRule = (uid, spawn);
                    break;
                }

                var lavalandRules = SEntMan.EntityQueryEnumerator<LavalandOutpostRuleComponent>();
                while (lavalandRules.MoveNext(out var uid, out var lavaland))
                {
                    lavalandRule = (uid, lavaland);
                    break;
                }
            });

            Assert.That(spawnRule, Is.Not.Null, "Roundstart did not add the mining shuttle rule.");
            Assert.That(lavalandRule, Is.Not.Null, "Roundstart did not run the Lavaland loader.");
            Assert.That(spawnRule!.Value.Comp.ShuttleGrid, Is.Not.Null, "Roundstart did not load the shuttle.");
            Assert.Multiple(() =>
            {
                Assert.That(spawnRule.Value.Comp.DockedToFallback, Is.True,
                    "A station without a tagged dock should fall back to the outpost.");
                Assert.That(Xform(spawnRule.Value.Comp.ShuttleGrid!.Value).MapID,
                    Is.EqualTo(lavalandRule!.Value.Comp.MapId!.Value));
            });
        }
        finally
        {
            await Pair.WaitCommand("golobby");
            await Server.WaitPost(() => ticker.SetGamePreset((GamePresetPrototype?) null));
            await Pair.WaitClientCommand("toggleready True");
            await Pair.WaitCommand("startround");
            await Pair.RunUntilSynced();
        }
    }

    [Test]
    public async Task SpawnRuleFallsBackToOutpost()
    {
        var ticker = Server.System<ServerGameTicker>();
        Entity<GameRuleComponent>? lavalandRule = null;
        Entity<GameRuleComponent>? shuttleRule = null;

        try
        {
            await Server.WaitPost(() => lavalandRule = ticker.AddGameRule(new EntProtoId("LavalandLoader")));
            Assert.That(lavalandRule, Is.Not.Null);
            var lavaland = SEntMan.GetComponent<LavalandOutpostRuleComponent>(lavalandRule!.Value.Owner);
            Assert.That(lavaland.MapId, Is.Not.Null);
            Assert.That(lavaland.OutpostGrid, Is.Not.Null);

            await Server.WaitPost(() => shuttleRule = ticker.AddGameRule(new EntProtoId("MiningShuttleLoader")));
            Assert.That(shuttleRule, Is.Not.Null);
            var spawnRule = SEntMan.GetComponent<MiningShuttleSpawnRuleComponent>(shuttleRule!.Value.Owner);

            Assert.That(spawnRule.ShuttleGrid, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(spawnRule.DockedToFallback, Is.True);
                Assert.That(Xform(spawnRule.ShuttleGrid!.Value).MapID, Is.EqualTo(lavaland.MapId!.Value));
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (shuttleRule is { } sr && SEntMan.EntityExists(sr.Owner))
                {
                    var spawnRule = SEntMan.GetComponent<MiningShuttleSpawnRuleComponent>(sr.Owner);
                    if (spawnRule.ShuttleGrid is { } shuttle && SEntMan.EntityExists(shuttle))
                        SEntMan.DeleteEntity(shuttle);
                    ticker.EndGameRule(sr);
                }

                if (lavalandRule is { } lr && SEntMan.EntityExists(lr.Owner))
                {
                    var lavaland = SEntMan.GetComponent<LavalandOutpostRuleComponent>(lr.Owner);
                    if (lavaland.Map is { } map && SEntMan.EntityExists(map))
                        SEntMan.DeleteEntity(map);
                    ticker.EndGameRule(lr);
                }
            });
        }
    }

    private TransformComponent Xform(EntityUid uid)
    {
        return SEntMan.GetComponent<TransformComponent>(uid);
    }

    private EntityUid GetStationGrid()
    {
        var stations = SEntMan.EntityQueryEnumerator<StationDataComponent>();
        var stationSystem = Server.System<ServerStationSystem>();
        while (stations.MoveNext(out var station, out var data))
        {
            if (stationSystem.GetLargestGrid((station, data)) is { } grid)
                return grid;
        }

        throw new InvalidOperationException("Test station has no grid.");
    }

    private async Task<EntityUid> LoadGrid(MapId mapId, string path)
    {
        EntityUid? grid = null;
        await Server.WaitPost(() =>
        {
            var loader = Server.System<MapLoaderSystem>();
            Assert.That(loader.TryLoadGrid(
                mapId,
                new ResPath(path),
                out var loaded,
                new DeserializationOptions { InitializeMaps = true }), $"Failed to load {path}.");
            grid = loaded;
        });
        Assert.That(grid, Is.Not.Null);
        return grid!.Value;
    }

    private async Task<EntityUid> SpawnMiningDock(EntityUid grid, Vector2 position)
    {
        EntityUid dock = default;
        await Server.WaitPost(() =>
        {
            var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);
            var indices = new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y));
            Server.System<SharedMapSystem>().SetTile(grid, gridComp, indices, new Tile(1));

            dock = SEntMan.SpawnEntity("AirlockShuttleMiningLocked", new EntityCoordinates(grid, position));
            var transform = Server.System<SharedTransformSystem>();
            transform.SetParent(dock, grid);
            var xform = SEntMan.GetComponent<TransformComponent>(dock);
            transform.AnchorEntity(dock, xform, gridComp);
        });
        return dock;
    }

    private async Task<Entity<MiningShuttleConsoleComponent>> FindConsole(EntityUid shuttle)
    {
        EntityUid? consoleUid = null;
        MiningShuttleConsoleComponent? consoleComp = null;
        await Server.WaitPost(() =>
        {
            var consoles = SEntMan.EntityQueryEnumerator<MiningShuttleConsoleComponent>();
            while (consoles.MoveNext(out var uid, out var console))
            {
                if (Xform(uid).GridUid == shuttle)
                {
                    consoleUid = uid;
                    consoleComp = console;
                    return;
                }
            }
        });

        Assert.That(consoleUid, Is.Not.Null, "Mining shuttle console was not found on the shuttle.");
        return (consoleUid!.Value, consoleComp!);
    }

    private async Task<HashSet<string>> EntityPrototypeIds(EntityUid grid)
    {
        HashSet<string>? ids = null;
        await Server.WaitPost(() =>
        {
            ids = new HashSet<string>();
            var query = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out _, out var meta, out var xform))
            {
                if (xform.GridUid == grid && meta.EntityPrototype is { } prototype)
                    ids.Add(prototype.ID);
            }
        });

        return ids!;
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
