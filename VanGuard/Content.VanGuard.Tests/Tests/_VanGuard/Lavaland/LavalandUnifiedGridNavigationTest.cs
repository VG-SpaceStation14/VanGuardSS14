using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._VanGuard.Lavaland.Components;
using Content.Server._VanGuard.Lavaland.Systems;
using Content.Server.NPC.Pathfinding;
using Content.Shared.CCVar;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.VanGuard.Tests.Tests._VanGuard.Lavaland;

/// <summary>
/// Covers navigation across the former POI boundary, after the POI was
/// folded into the planet's surface grid.
/// </summary>
/// <remarks>
/// The shared test pool disables <see cref="CCVars.PathfindingEnabled"/> for
/// performance (upstream "Add CVar to disable pathfinding"), so the tests
/// that actually query the pathfinding graph turn it back on for themselves.
/// Keep the opt-in limited to those tests: running with the graph enabled is
/// expensive and would slow down tests that only need physics.
/// </remarks>
[TestFixture]
public sealed class LavalandUnifiedGridNavigationTest : GameTest
{
    private static readonly ResPath ArenaPath = new("Maps/_VanGuard/Lavaland/Dungeons/bloodminerarena.yml");

    /// <summary>
    /// A path can be built from the planet surface into the POI and back.
    /// The POI is part of the surface grid now, so there is no gap and no
    /// portal is needed to get there.
    /// </summary>
    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.PathfindingEnabled), true)]
    public async Task PathIsBuiltAcrossTheFormerPoiBoundary()
    {
        EntityUid mapUid = default;
        EntityUid poiGrid = default;

        try
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            var loader = SEntMan.System<MapLoaderSystem>();

            await Server.WaitPost(() =>
            {
                mapUid = mapSystem.CreateMap(out var mapId);
                SEntMan.EnsureComponent<MapGridComponent>(mapUid);
                var mapGrid = new Entity<MapGridComponent>(mapUid, SEntMan.GetComponent<MapGridComponent>(mapUid));
                var tileMan = Server.Resolve<ITileDefinitionManager>();
                var floor = new Tile(tileMan["FloorBasalt"].TileId);
                for (var x = -8; x < 40; x++)
                    for (var y = -8; y < 40; y++)
                        mapSystem.SetTile(mapGrid, new Vector2i(x, y), floor);

                Assert.That(loader.TryLoadGrid(mapId, ArenaPath, out var grid,
                    new DeserializationOptions { InitializeMaps = true }), Is.True,
                    "Could not load the arena grid.");
                poiGrid = grid!.Value.Owner;

                // Put the POI in the middle of the surface, so the surface
                // surrounds it on every side.
                SEntMan.System<SharedTransformSystem>().SetLocalPosition(
                    SEntMan.GetComponent<TransformComponent>(poiGrid), new Vector2(16f, 16f));
            });

            var integrated = false;
            await Server.WaitPost(() =>
            {
                integrated = SEntMan.System<LavalandPoiGridIntegrationSystem>().Integrate(mapUid, poiGrid);
            });

            Assert.That(integrated, Is.True, "The POI grid was not integrated into the surface grid.");

            // The navmesh needs a moment to pick up the new tiles.
            await Pair.RunSeconds(2f);

            var results = new List<(PathResult Result, int Length)>();

            await Server.WaitPost(() =>
            {
                var pathfinding = SEntMan.System<PathfindingSystem>();
                var from = new EntityCoordinates(mapUid, new Vector2(4f, 18f));
                var into = new EntityCoordinates(mapUid, new Vector2(17f, 18f));
                var back = new EntityCoordinates(mapUid, new Vector2(4f, 18f));

                // The path request runs on the pathfinding worker, so it is
                // awaited outside of the server thread.
                CollectPath(pathfinding, from, into, results);
                CollectPath(pathfinding, into, back, results);
            });

            await Pair.RunSeconds(2f);

            Assert.That(results, Has.Count.EqualTo(2), "The path requests never completed.");

            foreach (var (result, _) in results)
                Assert.That(result, Is.EqualTo(PathResult.Path),
                    "No path was built across the former POI boundary.");

            // The paths must actually cross the boundary, not just be a stub.
            foreach (var (_, length) in results)
                Assert.That(length, Is.GreaterThan(1),
                    "The path across the former POI boundary has no steps.");
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

    /// <summary>
    /// A mob must be able to physically walk from the planet surface into the
    /// POI and back out again. This is the whole point of merging the POI
    /// into the surface grid: there is no gap left to fall into and no
    /// separate grid to be re-parented to.
    /// </summary>
    [Test]
    public async Task MobWalksAcrossTheFormerPoiBoundary()
    {
        var mapUid = EntityUid.Invalid;
        var poiGrid = EntityUid.Invalid;
        var steelTileId = 0;

        try
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            var loader = SEntMan.System<MapLoaderSystem>();

            await Server.WaitPost(() =>
            {
                mapUid = mapSystem.CreateMap(out var mapId);
                SEntMan.EnsureComponent<MapGridComponent>(mapUid);
                var mapGrid = new Entity<MapGridComponent>(mapUid, SEntMan.GetComponent<MapGridComponent>(mapUid));
                var tileMan = Server.Resolve<ITileDefinitionManager>();
                var floor = new Tile(tileMan["FloorSteel"].TileId);
                steelTileId = floor.TypeId;
                for (var x = -8; x < 40; x++)
                    for (var y = -8; y < 40; y++)
                        mapSystem.SetTile(mapGrid, new Vector2i(x, y), floor);

                Assert.That(loader.TryLoadGrid(mapId, ArenaPath, out var grid,
                    new DeserializationOptions { InitializeMaps = true }), Is.True,
                    "Could not load the arena grid.");
                poiGrid = grid!.Value.Owner;

                SEntMan.System<SharedTransformSystem>().SetLocalPosition(
                    SEntMan.GetComponent<TransformComponent>(poiGrid), new Vector2(16f, 16f));
            });

            // The tiles the POI brings with it, so we can tell the POI's
            // floor apart from the planet's floor once both share one grid.
            // The planet's floor is steel, so any tile of the POI that is not
            // steel belongs to the POI.
            var poiTileIds = new HashSet<int>();
            await Server.WaitPost(() =>
            {
                var gridComp = SEntMan.GetComponent<MapGridComponent>(poiGrid);
                foreach (var tile in mapSystem.GetAllTiles(poiGrid, gridComp))
                {
                    if (tile.Tile.TypeId == steelTileId)
                        continue;

                    poiTileIds.Add(tile.Tile.TypeId);
                }
            });

            Assert.That(poiTileIds, Is.Not.Empty, "The POI brought no tile of its own.");

            await Server.WaitPost(() =>
            {
                Assert.That(SEntMan.System<LavalandPoiGridIntegrationSystem>().Integrate(mapUid, poiGrid),
                    Is.True, "The POI grid was not integrated into the surface grid.");
            });

            // Find where the planet's floor meets the POI's own floor. That is
            // the former grid boundary, and the mob has to be able to walk
            // across it.
            var boundary = Vector2.Zero;
            var found = false;
            await Server.WaitPost(() =>
            {
                var mapGrid = SEntMan.GetComponent<MapGridComponent>(mapUid);
                for (var x = 10; x < 30; x++)
                    for (var y = 10; y < 30; y++)
                    {
                        if (!mapSystem.TryGetTile(mapGrid, new Vector2i(x, y), out var tile))
                            continue;

                        if (!poiTileIds.Contains(tile.TypeId))
                            continue;

                        // The POI's floor starts here. Walk west until the
                        // planet's own floor takes over.
                        var west = x;
                        while (west > 0)
                        {
                            if (!mapSystem.TryGetTile(mapGrid, new Vector2i(west - 1, y), out var westTile))
                                break;

                            if (poiTileIds.Contains(westTile.TypeId))
                            {
                                west--;
                                continue;
                            }

                            break;
                        }

                        boundary = new Vector2(x + 0.5f, y + 0.5f);
                        found = true;
                        return;
                    }
            });

            Assert.That(found, Is.True, "The POI's floor is nowhere on the planet's grid.");

            EntityUid mob = default;
            var poiBounds = Box2.Empty;

            await Server.WaitPost(() =>
            {
                // The mob starts right next to the former grid boundary, on
                // the planet's own floor.
                mob = SEntMan.SpawnEntity("MobHuman",
                    new EntityCoordinates(mapUid, new Vector2(boundary.X - 2.5f, boundary.Y)));
            });

            // The POI's footprint on the planet, as the marker recorded it.
            await Server.WaitPost(() =>
            {
                var query = SEntMan.EntityQueryEnumerator<LavalandPoiMarkerComponent>();
                while (query.MoveNext(out _, out var marker))
                {
                    poiBounds = marker.Bounds;
                    break;
                }
            });

            Assert.That(poiBounds != Box2.Empty, "The POI did not leave a marker behind.");

            await Pair.RunSeconds(1f);

            // Walk east, onto the POI's floor.
            await Server.WaitPost(() =>
            {
                SEntMan.EnsureComponent<Content.Shared.Movement.Components.InputMoverComponent>(mob);
                var mover = SEntMan.GetComponent<Content.Shared.Movement.Components.InputMoverComponent>(mob);
                mover.HeldMoveButtons = Content.Shared.Movement.Systems.MoveButtons.Right;
                mover.WishDir = Vector2.UnitX;
            });

            var reachedInside = false;
            var insidePosition = Vector2.Zero;
            var gridStayedValid = true;

            for (var i = 0; i < 24; i++)
            {
                await Pair.RunSeconds(0.5f);

                var (position, gridUid, _, _) = await ReadMob(mob);

                if (gridUid == EntityUid.Invalid)
                    gridStayedValid = false;

                // The mob has entered the POI as soon as it stands on a tile
                // that the POI brought with it.
                if (position.X >= boundary.X)
                {
                    reachedInside = true;
                    insidePosition = position;
                    break;
                }
            }

            Assert.That(gridStayedValid, Is.True,
                "The mob lost its grid while crossing the former POI boundary.");
            Assert.That(reachedInside, Is.True,
                "The mob never made it into the POI. It stopped at " + insidePosition + ".");

            // The mob must be on the surface grid now, not on a POI grid.
            var (_, mobGrid, mobMap, _) = await ReadMob(mob);
            Assert.That(mobGrid, Is.EqualTo(mapUid),
                "The mob is not on the planet's surface grid after the crossing.");
            Assert.That(mobMap, Is.EqualTo(SEntMan.GetComponent<MapComponent>(mapUid).MapId));

            // The mob has to end up standing on the POI's own floor, not on
            // the planet's floor that used to be underneath it.
            var standsOnPoiTile = false;
            var stoodOnTileId = 0;
            await Server.WaitPost(() =>
            {
                var mapGrid = SEntMan.GetComponent<MapGridComponent>(mapUid);
                var indices = new Vector2i((int) MathF.Floor(insidePosition.X), (int) MathF.Floor(insidePosition.Y));
                if (mapSystem.TryGetTile(mapGrid, indices, out var tile))
                {
                    stoodOnTileId = tile.TypeId;
                    standsOnPoiTile = poiTileIds.Contains(tile.TypeId);
                }
            });

            Assert.That(standsOnPoiTile, Is.True,
                $"The mob does not stand on the POI's own floor " +
                $"(tile {stoodOnTileId}, expected one of {string.Join(",", poiTileIds)}).");

            // Now walk back out again.
            await Server.WaitPost(() =>
            {
                var mover = SEntMan.GetComponent<Content.Shared.Movement.Components.InputMoverComponent>(mob);
                mover.HeldMoveButtons = Content.Shared.Movement.Systems.MoveButtons.Left;
                mover.WishDir = -Vector2.UnitX;
            });

            var returned = false;
            for (var i = 0; i < 24; i++)
            {
                await Pair.RunSeconds(0.5f);

                var (position, gridUid, _, _) = await ReadMob(mob);

                if (gridUid == EntityUid.Invalid)
                    gridStayedValid = false;

                // The mob is back on the planet's own floor once it is west of
                // the POI's floor again.
                if (position.X < boundary.X - 1f)
                {
                    returned = true;
                    break;
                }
            }

            Assert.That(gridStayedValid, Is.True,
                "The mob lost its grid while walking back out of the POI.");
            Assert.That(returned, Is.True,
                "The mob never made it back out of the POI.");
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

    private async Task<(Vector2 Position, EntityUid Grid, MapId Map, Vector2 Local)> ReadMob(EntityUid mob)
    {
        var result = (Position: Vector2.Zero, Grid: EntityUid.Invalid, Map: MapId.Nullspace, Local: Vector2.Zero);

        await Server.WaitPost(() =>
        {
            var xform = SEntMan.GetComponent<TransformComponent>(mob);
            result = (xform.WorldPosition, xform.GridUid ?? EntityUid.Invalid, xform.MapID, xform.LocalPosition);
        });

        return result;
    }

    /// <summary>
    /// Requests a path and stores the result once the pathfinding worker is
    /// done with it.
    /// </summary>
    private static void CollectPath(
        PathfindingSystem pathfinding,
        EntityCoordinates from,
        EntityCoordinates to,
        List<(PathResult Result, int Length)> results)
    {
        var task = pathfinding.GetPath(from, to, 0f, 0, 0, default);
        task.ContinueWith(completed =>
        {
            var event_ = completed.GetAwaiter().GetResult();
            lock (results)
            {
                results.Add((event_.Result, event_.Path.Count));
            }
        });
    }
}
