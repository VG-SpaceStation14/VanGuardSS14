#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC.Pathfinding;
using Content.Shared.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.VanGuard.Tests.Tests._VanGuard.Lavaland;

/// <summary>
/// Covers ground mob navigation on the Lavaland surface: lava and chasm
/// tiles must be treated as impassable by the pathfinder for ordinary
/// walkers, while mobs that can fly (or otherwise opt in) cross them
/// freely.
/// </summary>
[TestFixture]
public sealed class LavalandHazardNavigationTest : GameTest
{
    /// <summary>
    /// A path between two points separated only by a wall of lava tiles is
    /// not built: without <see cref="PathFlags.Hazards"/> the hazardous
    /// tile is impassable and the A* frontier stops expanding into it.
    /// </summary>
    [Test]
    public async Task GroundMobPathDoesNotCrossLava()
    {
        await AssertHazardIsImpassable("FloorLavaEntity");
    }

    /// <summary>
    /// Same as <see cref="GroundMobPathDoesNotCrossLava"/> but for a chasm:
    /// a mob would fall in and be deleted, so no path may lead through it.
    /// </summary>
    [Test]
    public async Task GroundMobPathDoesNotCrossChasm()
    {
        await AssertHazardIsImpassable("FloorChasmEntity");
    }

    /// <summary>
    /// A mob with <see cref="PathFlags.Hazards"/> (flying mobs and anyone
    /// with the NavHazards blackboard key) keeps its path across the same
    /// hazard, so crossings stay possible for the entities that are
    /// actually immune.
    /// </summary>
    [Test]
    public async Task HazardCapableMobPathStillCrosses()
    {
        var results = await BuildPathAcrossHazard("FloorLavaEntity", PathFlags.Hazards, "MobBee");

        Assert.That(results.Count, Is.EqualTo(1), "The path request never completed.");
        Assert.That(results[0].Result, Is.EqualTo(PathResult.Path),
            "A hazard-capable mob must still get a path across the lava wall.");
        Assert.That(results[0].Hazards, Is.Not.Empty,
            "Test setup error: the lava wall was not built.");
    }

    /// <summary>
    /// Flying mobs (e.g. <c>MobBee</c>, which inherits <c>FlyingMobBase</c>
    /// with <c>CanMoveInAir</c>) automatically get
    /// <see cref="PathFlags.Hazards"/> from the pathfinder, so no per-mob
    /// special-casing is needed for them.
    /// </summary>
    [Test]
    public async Task FlyingMobGetsHazardFlags()
    {
        EntityUid mapUid = default;
        var flags = PathFlags.None;

        try
        {
            await Server.WaitPost(() =>
            {
                var mapSystem = SEntMan.System<SharedMapSystem>();
                mapUid = mapSystem.CreateMap(out _);
                var mob = SEntMan.SpawnEntity("MobBee", new EntityCoordinates(mapUid, Vector2.Zero));
                flags = SEntMan.System<PathfindingSystem>().GetFlags(mob);
            });

            Assert.That((flags & PathFlags.Hazards) != 0x0, Is.True,
                "Flying mobs must receive PathFlags.Hazards so their pathfinder may cross lava and chasms.");
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (SEntMan.EntityExists(mapUid))
                    SEntMan.DeleteEntity(mapUid);
            });
        }
    }

    private async Task AssertHazardIsImpassable(string hazardProto)
    {
        // MobMonkey is a plain ground NPC without CanMoveInAir, so the
        // pathfinder does not grant it PathFlags.Hazards.
        var results = await BuildPathAcrossHazard(hazardProto, PathFlags.None, "MobMonkey");

        Assert.That(results.Count, Is.EqualTo(1), "The path request never completed.");
        Assert.That(results[0].Result, Is.EqualTo(PathResult.NoPath),
            $"A ground mob must not get a path through the {hazardProto} wall.");
        Assert.That(results[0].Hazards, Is.Not.Empty,
            "Test setup error: the hazard wall was not built.");
    }

    /// <summary>
    /// Builds a 16x10 surface grid split in two by a full-height wall of
    /// hazard tiles at x = 7, then asks the pathfinder for a path from the
    /// left half to the right half. Since the wall spans the whole grid,
    /// the only way across is through it.
    /// </summary>
    private async Task<List<(PathResult Result, List<PathPoly> Hazards)>> BuildPathAcrossHazard(
        string hazardProto,
        PathFlags flags,
        string mobProto)
    {
        EntityUid mapUid = default;

        try
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            var pathfinding = SEntMan.System<PathfindingSystem>();

            await Server.WaitPost(() =>
            {
                mapUid = mapSystem.CreateMap(out var mapId);
                SEntMan.EnsureComponent<MapGridComponent>(mapUid);
                var mapGrid = new Entity<MapGridComponent>(mapUid, SEntMan.GetComponent<MapGridComponent>(mapUid));
                var tileMan = Server.Resolve<ITileDefinitionManager>();
                var floor = new Tile(tileMan["FloorBasalt"].TileId);

                for (var x = 0; x < 16; x++)
                {
                    for (var y = 0; y < 10; y++)
                        mapSystem.SetTile(mapGrid, new Vector2i(x, y), floor);
                }

                // Hazard wall: x = 7, all y. It divides the grid into two
                // halves, so the only route from one side to the other is
                // through the hazard itself.
                for (var y = 0; y < 10; y++)
                {
                    SEntMan.SpawnEntity(hazardProto,
                        new EntityCoordinates(mapUid, new Vector2(7.5f, y + 0.5f)));
                }
            });

            // Give the navmesh a moment to rebuild with the hazard tiles.
            await Pair.RunSeconds(1f);

            var results = new List<(PathResult Result, List<PathPoly> Hazards)>();
            var hazards = new List<PathPoly>();

            await Server.WaitPost(() =>
            {
                // Sanity check that the mob's own hazard capability matches
                // the flags under test, so the two test cases really differ
                // only in the flag.
                var mob = SEntMan.SpawnEntity(mobProto,
                    new EntityCoordinates(mapUid, new Vector2(2.5f, 5.5f)));
                var mobFlags = pathfinding.GetFlags(mob);

                Assert.That((mobFlags & PathFlags.Hazards) != 0x0,
                    Is.EqualTo((flags & PathFlags.Hazards) != 0x0),
                    $"Test setup error: {mobProto} hazard capability does not match the requested PathFlags.");

                SEntMan.DeleteEntity(mob);

                for (var y = 0; y < 10; y++)
                {
                    var poly = pathfinding.GetPoly(new EntityCoordinates(mapUid, new Vector2(7.5f, y + 0.5f)));
                    if (poly != null)
                        hazards.Add(poly);
                }

                var task = pathfinding.GetPath(
                    new EntityCoordinates(mapUid, new Vector2(2.5f, 5.5f)),
                    new EntityCoordinates(mapUid, new Vector2(13.5f, 5.5f)),
                    0f, 0, 0, default, flags);

                task.ContinueWith(completed =>
                {
                    var path = completed.GetAwaiter().GetResult();
                    lock (results)
                    {
                        results.Add((path.Result, new List<PathPoly>(hazards)));
                    }
                });
            });

            await Pair.RunSeconds(2f);
            return results;
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (SEntMan.EntityExists(mapUid))
                    SEntMan.DeleteEntity(mapUid);
            });
        }
    }
}