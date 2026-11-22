using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._VanGuard.Lavaland.Systems;
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
/// Covers folding a loaded Lavaland POI grid into the planet's surface grid.
/// </summary>
[TestFixture]
public sealed class LavalandPoiIntegrationTest : GameTest
{
    private static readonly ResPath ArenaPath = new("Maps/_VanGuard/Lavaland/Dungeons/bloodminerarena.yml");
    private static readonly ResPath LavaRiverPath = new("Maps/_VanGuard/Lavaland/Dungeons/lava_river.yml");

    /// <summary>
    /// The surface tiles under the POI are removed, so the POI's own tiles
    /// replace them instead of overlapping.
    /// </summary>
    [Test]
    public async Task SurfaceTilesUnderPoiAreReplaced()
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
                var floor = new Tile(tileMan["FloorSteel"].TileId);
                for (var x = 0; x < 24; x++)
                    for (var y = 0; y < 24; y++)
                        mapSystem.SetTile(mapGrid, new Vector2i(x, y), floor);

                Assert.That(loader.TryLoadGrid(mapId, ArenaPath, out var grid,
                    new DeserializationOptions { InitializeMaps = true }), Is.True,
                    "Could not load the arena grid.");
                poiGrid = grid!.Value.Owner;
            });

            var offsetTile = Vector2i.Zero;
            var poiTileIndices = new List<Vector2i>();

            await Server.WaitPost(() =>
            {
                var poiXform = SEntMan.GetComponent<TransformComponent>(poiGrid);
                offsetTile = new Vector2i(
                    (int) MathF.Floor(poiXform.WorldPosition.X),
                    (int) MathF.Floor(poiXform.WorldPosition.Y));

                var poiGridComp = SEntMan.GetComponent<MapGridComponent>(poiGrid);
                var mapSystem = SEntMan.System<SharedMapSystem>();
                foreach (var tile in mapSystem.GetAllTiles(poiGrid, poiGridComp))
                    poiTileIndices.Add(tile.GridIndices);
            });

            Assert.That(poiTileIndices, Is.Not.Empty);

            await Server.WaitPost(() =>
            {
                SEntMan.System<LavalandPoiGridIntegrationSystem>().Integrate(mapUid, poiGrid);
            });

            await Server.WaitPost(() =>
            {
                var mapGrid = SEntMan.GetComponent<MapGridComponent>(mapUid);
                var mapSystem = SEntMan.System<SharedMapSystem>();
                var tileMan = Server.Resolve<ITileDefinitionManager>();
                var surfaceType = tileMan["FloorSteel"].TileId;

                foreach (var indices in poiTileIndices)
                {
                    var expected = new Vector2i(indices.X + offsetTile.X, indices.Y + offsetTile.Y);
                    Assert.That(mapSystem.TryGetTile(mapGrid, expected, out var tile), Is.True,
                        $"Surface tile {expected} is missing after the integration.");

                    // The POI's own tile type has to win: the surface tile
                    // underneath must not survive.
                    Assert.That(tile.TypeId, Is.Not.EqualTo(surfaceType),
                        $"Surface tile {expected} was not replaced by the POI's tile.");
                }
            });
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
    /// A second POI can be merged into the same surface grid afterwards, and
    /// the first POI's content is unaffected.
    /// </summary>
    [Test]
    public async Task SeveralPoisAreMergedIntoTheSameSurfaceGrid()
    {
        EntityUid mapUid = default;

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
                for (var x = 0; x < 48; x++)
                    for (var y = 0; y < 48; y++)
                        mapSystem.SetTile(mapGrid, new Vector2i(x, y), floor);
            });

            var grids = new List<EntityUid>();

            await Server.WaitPost(() =>
            {
                var mapId = SEntMan.GetComponent<TransformComponent>(mapUid).MapID;
                var loader = SEntMan.System<MapLoaderSystem>();

                Assert.That(loader.TryLoadGrid(mapId, ArenaPath, out var arena,
                    new DeserializationOptions { InitializeMaps = true }), Is.True);
                grids.Add(arena!.Value.Owner);

                Assert.That(loader.TryLoadGrid(mapId, LavaRiverPath, out var river,
                    new DeserializationOptions { InitializeMaps = true }), Is.True);
                grids.Add(river!.Value.Owner);
            });

            // Place the two POIs apart from each other.
            await Server.WaitPost(() =>
            {
                var transform = SEntMan.System<SharedTransformSystem>();
                transform.SetLocalPosition(SEntMan.GetComponent<TransformComponent>(grids[0]), new Vector2(4f, 4f));
                transform.SetLocalPosition(SEntMan.GetComponent<TransformComponent>(grids[1]), new Vector2(24f, 24f));
            });

            var results = new List<bool>();
            await Server.WaitPost(() =>
            {
                var integration = SEntMan.System<LavalandPoiGridIntegrationSystem>();
                results.Add(integration.Integrate(mapUid, grids[0]));
                results.Add(integration.Integrate(mapUid, grids[1]));
            });

            Assert.That(results, Is.All.True, "Not every POI was integrated.");

            await Server.WaitPost(() =>
            {
                Assert.That(SEntMan.EntityExists(grids[0]), Is.False,
                    "The first POI grid still exists after the integration.");
                Assert.That(SEntMan.EntityExists(grids[1]), Is.False,
                    "The second POI grid still exists after the integration.");

                // Nothing may still be parented to the deleted grids.
                var query = SEntMan.EntityQueryEnumerator<TransformComponent>();
                while (query.MoveNext(out var uid, out var xform))
                {
                    Assert.That(xform.ParentUid, Is.Not.EqualTo(grids[0]),
                        $"Entity {uid} still references the deleted first POI grid.");
                    Assert.That(xform.ParentUid, Is.Not.EqualTo(grids[1]),
                        $"Entity {uid} still references the deleted second POI grid.");
                    Assert.That(xform.GridUid, Is.Not.EqualTo(grids[0]),
                        $"Entity {uid} is still on the deleted first POI grid.");
                    Assert.That(xform.GridUid, Is.Not.EqualTo(grids[1]),
                        $"Entity {uid} is still on the deleted second POI grid.");
                }
            });
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
