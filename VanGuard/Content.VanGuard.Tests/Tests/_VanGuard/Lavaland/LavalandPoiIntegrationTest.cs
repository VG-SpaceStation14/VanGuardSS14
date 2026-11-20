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
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
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
    /// A POI grid is merged into the surface grid: its tiles end up on the
    /// surface, its entities are re-parented to the surface grid, and the
    /// temporary grid entity is gone.
    /// </summary>
    [Test]
    public async Task PoiGridIsMergedIntoSurfaceGrid()
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
                // The planet surface is the map entity's own grid, exactly like
                // BiomeSystem.EnsurePlanet sets it up in production.
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
                Assert.That(grid, Is.Not.Null);
                poiGrid = grid!.Value.Owner;
            });

            // Remember what the POI looks like before the merge.
            var entitiesBefore = new List<PoiEntity>();
            var tilesBefore = new Dictionary<Vector2i, int>();
            var offsetTile = Vector2i.Zero;

            await Server.WaitPost(() =>
            {
                var poiGridComp = SEntMan.GetComponent<MapGridComponent>(poiGrid);
                var poiXform = SEntMan.GetComponent<TransformComponent>(poiGrid);

                // The integration snaps the POI onto the surface's tile
                // lattice before merging, so the effective offset is the
                // floor of the POI origin.
                offsetTile = new Vector2i(
                    (int) MathF.Floor(poiXform.WorldPosition.X),
                    (int) MathF.Floor(poiXform.WorldPosition.Y));

                var mapSystem = SEntMan.System<SharedMapSystem>();
                foreach (var tile in mapSystem.GetAllTiles(poiGrid, poiGridComp))
                    tilesBefore[tile.GridIndices] = tile.Tile.TypeId;

                var query = SEntMan.EntityQueryEnumerator<TransformComponent>();
                while (query.MoveNext(out var uid, out var xform))
                {
                    if (xform.GridUid != poiGrid)
                        continue;

                    // The grid entity itself is not POI content: the merge is
                    // expected to delete it.
                    if (uid == poiGrid)
                        continue;

                    var parentWorld = Vector2.Zero;
                    if (xform.ParentUid != poiGrid && xform.ParentUid != EntityUid.Invalid)
                    {
                        if (SEntMan.TryGetComponent<TransformComponent>(xform.ParentUid, out var parentXform))
                            parentWorld = parentXform.WorldPosition;
                    }

                    entitiesBefore.Add(new PoiEntity(
                        uid,
                        xform.ParentUid,
                        xform.WorldPosition,
                        xform.LocalPosition,
                        xform.LocalRotation,
                        xform.Anchored,
                        parentWorld));
                }
            });

            Assert.That(tilesBefore, Is.Not.Empty, "The arena grid had no tiles to begin with.");
            Assert.That(entitiesBefore, Is.Not.Empty, "The arena grid had no entities to begin with.");

            var integrated = false;
            await Server.WaitPost(() =>
            {
                integrated = SEntMan.System<LavalandPoiGridIntegrationSystem>().Integrate(mapUid, poiGrid);
            });

            Assert.That(integrated, Is.True, "The POI grid was not integrated into the surface grid.");

            await Server.WaitPost(() =>
            {
                // The POI grid entity must be gone.
                Assert.That(SEntMan.EntityExists(poiGrid), Is.False,
                    "The POI grid entity still exists after the integration.");

                var mapGrid = SEntMan.GetComponent<MapGridComponent>(mapUid);
                var mapSystem = SEntMan.System<SharedMapSystem>();

                // Every POI tile must now exist on the surface grid, with the
                // same tile type it had on the POI grid.
                foreach (var (indices, typeId) in tilesBefore)
                {
                    var expected = new Vector2i(indices.X + offsetTile.X, indices.Y + offsetTile.Y);

                    Assert.That(mapSystem.TryGetTile(mapGrid, expected, out var tile), Is.True,
                        $"POI tile {indices} is missing on the surface grid at {expected}.");
                    Assert.That(tile.TypeId, Is.EqualTo(typeId),
                        $"POI tile {indices} changed its type when it was moved to {expected}.");
                }

                // No entity may be lost.
                foreach (var entity in entitiesBefore)
                {
                    Assert.That(SEntMan.EntityExists(entity.Uid), Is.True,
                        $"POI entity {entity.Uid} was destroyed by the integration.");
                }

                // The hierarchy-aware position check: direct children of the
                // grid move by the tile offset, nested children follow their
                // parent.
                var moved = new Dictionary<EntityUid, Vector2>();
                foreach (var entity in entitiesBefore.OrderBy(e => e.Parent == poiGrid ? 0 : 1))
                {
                    var xform = SEntMan.GetComponent<TransformComponent>(entity.Uid);

                    Assert.That(xform.GridUid, Is.EqualTo(mapUid),
                        $"POI entity {entity.Uid} is still on grid {xform.GridUid} instead of the surface grid.");

                    Vector2 expected;
                    if (entity.Parent == poiGrid)
                    {
                        if (entity.Anchored)
                        {
                            // Anchored entities are re-anchored onto the
                            // matching surface tile, which snaps them to that
                            // tile's centre. That is the engine's own
                            // behaviour and it is what keeps them glued to
                            // the planet.
                            var localTile = new Vector2i(
                                (int) MathF.Floor(entity.Local.X),
                                (int) MathF.Floor(entity.Local.Y));
                            expected = new Vector2(
                                localTile.X + offsetTile.X + 0.5f,
                                localTile.Y + offsetTile.Y + 0.5f);
                        }
                        else
                        {
                            // Unanchored entities keep their position inside
                            // the POI's tile space, moved onto the surface
                            // grid's tile space by the same offset the tiles
                            // use.
                            expected = new Vector2(
                                entity.Local.X + offsetTile.X,
                                entity.Local.Y + offsetTile.Y);
                        }
                    }
                    else
                    {
                        // Nested entities follow their parent: they keep
                        // their offset to it. The parent's own shift is the
                        // shift the merge applied to it.
                        Assert.That(moved.TryGetValue(entity.Parent, out var parentAfter), Is.True,
                            $"POI entity {entity.Uid} has no moved parent to follow.");
                        var parentShift = parentAfter - entity.ParentWorld;
                        expected = entity.World + parentShift;
                    }

                    moved[entity.Uid] = xform.WorldPosition;

                    Assert.That(xform.WorldPosition.X, Is.EqualTo(expected.X).Within(0.02f),
                        $"POI entity {entity.Uid} moved during the integration. " +
                        $"Expected {expected}, was {xform.WorldPosition}.");
                    Assert.That(xform.WorldPosition.Y, Is.EqualTo(expected.Y).Within(0.02f),
                        $"POI entity {entity.Uid} moved during the integration. " +
                        $"Expected {expected}, was {xform.WorldPosition}.");
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

    private readonly record struct PoiEntity(
        EntityUid Uid,
        EntityUid Parent,
        Vector2 World,
        Vector2 Local,
        Angle Rotation,
        bool Anchored,
        Vector2 ParentWorld);
}
