using System.Linq;
using System.Numerics;
using Content.Server._VanGuard.Lavaland.Components;
using Content.Server.Parallax;
using Content.Shared.Parallax;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Shuttles.Components;
using Robust.Server.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._VanGuard.Lavaland.Systems;

/// <summary>
/// Folds a freshly loaded Lavaland POI grid into the planet's surface grid so
/// that the whole planet ends up as a single <see cref="MapGridComponent"/>.
///
/// Lavaland POIs are authored as standalone map files that the loader turns
/// into their own grid entity. That leaves every POI floating above the
/// planet surface: the POI's tiles and entities live in a separate grid, so
/// the surface keeps its own tiles underneath and the two never share a tile
/// space. NPCs therefore cannot walk between the surface and a POI, because
/// <c>SharedGridTraversalSystem</c> only reparents an entity onto a grid that
/// actually owns the tile underneath it, and the pathfinder builds its
/// navmesh per grid.
///
/// The engine already ships a mechanism for exactly this:
/// <see cref="GridFixtureSystem.Merge"/>. It transfers the tiles of grid B
/// into grid A under a matrix transform, re-anchors anchored entities onto
/// the matching tile of grid A, moves unanchored entities over while keeping
/// their world position, and finally deletes grid B. This system works out
/// the transform and clears the surface tiles that the POI takes over.
/// </summary>
public sealed partial class LavalandPoiGridIntegrationSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private GridFixtureSystem _fixtures = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private BiomeSystem _biome = default!;

    /// <summary>
    /// Integrates <paramref name="poiGrid"/> into the planet surface grid of
    /// <paramref name="mapUid"/>.
    /// </summary>
    /// <param name="mapUid">The planet map entity. Its own
    /// <see cref="MapGridComponent"/> is the surface grid.</param>
    /// <param name="poiGrid">The temporary grid entity of the loaded POI.</param>
    /// <param name="name">Localization key of the POI's name.</param>
    /// <param name="faxName">Localization key of the POI's survey fax
    /// designation.</param>
    /// <param name="unique">True for unique structures rather than ordinary
    /// dungeons.</param>
    /// <returns>True if the POI was integrated.</returns>
    public bool Integrate(
        EntityUid mapUid,
        EntityUid poiGrid,
        string name = "",
        string faxName = "",
        bool unique = false)
    {
        if (!Exists(mapUid) || !Exists(poiGrid))
            return false;

        if (!TryComp<MapGridComponent>(mapUid, out var surfaceGrid))
            return false;

        if (!TryComp<MapGridComponent>(poiGrid, out var poiGridComp))
            return false;

        if (!TryComp(mapUid, out TransformComponent? surfaceXform))
            return false;

        if (!TryComp(poiGrid, out TransformComponent? poiXform))
            return false;

        if (surfaceXform.MapID != poiXform.MapID)
            return false;

        // Any child grids parented to the POI grid have to be folded in first,
        // otherwise their tile contents stay stranded when the POI grid is
        // deleted by the merge. Merging nested grids first preserves their
        // anchoring and transform hierarchy.
        var childGrids = new List<Entity<MapGridComponent>>();
        var gridQuery = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var childUid, out var childGridComp, out var childXform))
        {
            if (childUid == poiGrid || childUid == mapUid)
                continue;

            if (childXform.ParentUid != poiGrid)
                continue;

            childGrids.Add(new Entity<MapGridComponent>(childUid, childGridComp));
        }

        foreach (var child in childGrids)
        {
            if (!IntegrateChild(mapUid, child, poiGrid))
            {
                Log.Warning(
                    $"Could not integrate nested Lavaland grid {child} into its parent before the POI merge.");
            }
        }

        // The surface grid is the map entity's own grid, so its tile indices
        // are world tile indices and its origin sits in world space.
        var surfaceOrigin = surfaceXform.WorldPosition;
        var surfaceRotation = surfaceXform.WorldRotation;

        // The POI grid is a child of the map entity, so its world position
        // already includes the map's own transform. Everything below is
        // computed in the surface grid's local tile space.
        var poiOrigin = poiXform.WorldPosition;

        // Work out where the POI's local tile (0, 0) lands in the surface
        // grid's tile space. The relative position is rotated into the
        // surface frame first so the surface's own rotation cancels out.
        var poiRotation = poiXform.WorldRotation;
        var relative = poiOrigin - surfaceOrigin;
        var inSurfaceFrame = (-surfaceRotation).RotateVec(relative);

        // The merge matrix maps grid B's tile indices into grid A's tile index
        // space and quantises the result to whole tiles. For the POI's tiles
        // to land on the surface's tile lattice one-to-one, the POI's origin
        // has to sit on a lattice point of the surface grid.
        //
        // Snapping the POI onto that lattice is what makes the merge exact:
        // the tile indices map one-to-one and the POI keeps the geometry it
        // was authored with, instead of being shifted by up to a whole tile.
        var snappedFrame = new Vector2(
            (float) MathF.Floor(inSurfaceFrame.X),
            (float) MathF.Floor(inSurfaceFrame.Y));

        // Move the POI grid onto the snapped position. The snapped offset is
        // expressed in the surface frame and rotated back into world space.
        var snappedWorld = surfaceRotation.RotateVec(snappedFrame) + surfaceOrigin;
        var correction = snappedWorld - poiOrigin;

        if (correction != Vector2.Zero)
        {
            _transform.SetLocalPosition(poiXform, poiXform.LocalPosition + correction);
            poiOrigin = snappedWorld;
        }

        // The merge matrix rotates grid B around its origin and then
        // translates it. Grid B's rotation is the difference between the POI
        // and the surface, which is what we hand to the merge.
        var mergeRotation = poiRotation - surfaceRotation;

        // The tile index the POI's origin lands on in the surface grid.
        var offsetTile = snappedFrame.Floored();

        // Clear the surface tiles that the POI is about to occupy. Without
        // this the planet would keep its own tiles underneath the POI and
        // both tile sets would collide in the same space.
        ClearSurfaceTiles(surfaceGrid, poiGridComp, offsetTile, mergeRotation);

        // Remember where the POI ended up. The grid entity is about to
        // disappear, so this is the only way left to find the POI again.
        var poiBounds = poiGridComp.LocalAABB.Translated(offsetTile);

        // Sweep the planet's own scenery out of the POI's footprint. The
        // planet generates rocks, lava and flora on its surface tiles, and
        // those would end up embedded in the POI's walls.
        ClearSurfaceEntities(mapUid, poiBounds);

        // The engine's merge reparents entities that fall inside any tile of
        // grid B (via GetLocalEntitiesIntersecting) or any anchor on a tile
        // (via ReAnchor). Children whose local position lies outside the
        // grid's tile AABB are unreachable through that lookup, so they
        // would stay parented to grid B and trip the [Conditional("DEBUG")]
        // assert on xformB.ChildCount inside GridFixtureSystem.Merge under
        // DebugOpt. Re-anchor those leftovers only, leaving the engine to
        // handle anchoring and in-bounds placement as it normally would.
        //
        // The set of children has to be materialised before any of them are
        // reparented, because SetCoordinates mutates the parent's child list
        // and would invalidate the enumerator mid-loop.
        var leftoverTargets = new List<(EntityUid Uid, Vector2 WorldPos, Angle WorldRot)>();
        var leftoverQuery = EntityQueryEnumerator<TransformComponent>();
        while (leftoverQuery.MoveNext(out var childUid, out var childXform))
        {
            if (childUid == poiGrid || childUid == mapUid)
                continue;

            if (childXform.ParentUid != poiGrid)
                continue;

            // Skip nested grids; those are folded by IntegrateChild above so
            // that their tile contents land on the surface grid, not lost.
            if (HasComp<MapGridComponent>(childUid))
                continue;

            // The engine's own loop iterates the tiles of grid B and asks the
            // broadphase for entities intersecting each tile's bounds. A
            // child whose local position lies outside every tile's bounds
            // (i.e. outside the grid's local AABB) is invisible to that
            // lookup and would remain parented to grid B after Merge.
            if (poiGridComp.LocalAABB.Contains(childXform.LocalPosition))
                continue;

            // Anchored children outside the AABB belong to the snapgrid of
            // grid B's surrounding tiles. The engine's merge does not detach
            // them either, but they are rare in practice (cables placed near
            // the grid edge, etc.) and reparenting them via SetCoordinates
            // would strip their anchoring, leaving them floating. Leave them
            // alone for now: the dirty-dispose path this whole loop exists to
            // fix is only triggered by unanchored leftovers.
            if (childXform.Anchored)
                continue;

            leftoverTargets.Add((childUid, childXform.WorldPosition, childXform.WorldRotation));
        }

        var reparented = 0;
        foreach (var target in leftoverTargets)
        {
            _transform.SetCoordinates(
                target.Uid,
                new EntityCoordinates(mapUid, target.WorldPos));
            _transform.SetWorldRotation(target.Uid, target.WorldRot);
            reparented++;
        }

        if (reparented > 0)
            Log.Debug($"Reparented {reparented} POI children outside the tile AABB onto the surface grid before merge.");

        // Now fold the POI in. The engine transfers the tiles, re-anchors the
        // anchored entities onto the matching surface tile, moves the
        // unanchored ones while keeping their world position, and deletes the
        // temporary grid.
        _fixtures.Merge(
            mapUid,
            poiGrid,
            offsetTile,
            mergeRotation,
            surfaceGrid,
            poiGridComp,
            surfaceXform,
            poiXform);

        // The biome must not grow back into the POI. The merge writes the
        // POI's tiles straight into the surface grid, so the biome has to be
        // told that this area is settled, otherwise it would regenerate its
        // own lava and rock over the POI's floor. This has to happen after
        // the merge, because the reservation only takes effect for tiles
        // that actually exist.
        ReserveBiomeArea(mapUid, poiBounds);

        // The merged grid must not keep shuttle physics any more, it is plain
        // planet geometry now.
        RemComp<ShuttleComponent>(mapUid);
        if (TryComp<PhysicsComponent>(mapUid, out var physics))
            _physics.SetBodyType(mapUid, BodyType.Static, body: physics);

        // Leave a marker behind so the POI stays identifiable now that its
        // grid entity is gone. The marker sits at the POI's centre, so it
        // also answers "where is this POI" without a grid of its own.
        var marker = Spawn(null, new EntityCoordinates(mapUid, poiBounds.Center));
        var markerComp = EnsureComp<LavalandPoiMarkerComponent>(marker);
        markerComp.Name = name;
        markerComp.FaxName = faxName;
        markerComp.Unique = unique;
        markerComp.Bounds = poiBounds;

        return true;
    }

    /// <summary>
    /// Tells the planet's biome that the given area is settled, so it cannot
    /// regenerate its own terrain over the POI's floor.
    /// </summary>
    private void ReserveBiomeArea(EntityUid mapUid, Box2 bounds)
    {
        if (!TryComp<BiomeComponent>(mapUid, out var biome))
            return;

        if (!TryComp<MapGridComponent>(mapUid, out var surfaceGrid))
            return;

        // The biome works on its own chunk size, which is unrelated to the
        // grid's, so the reservation has to go through the biome's own API.
        _biome.ReserveTiles(mapUid, bounds, new List<(Vector2i, Tile)>(), biome, surfaceGrid);
    }

    /// <summary>
    /// Folds a child grid into its parent grid, preserving tile alignment.
    /// </summary>
    private bool IntegrateChild(
        EntityUid parentGrid,
        Entity<MapGridComponent> child,
        EntityUid hostGrid)
    {
        if (!TryComp(parentGrid, out TransformComponent? parentXform))
            return false;

        if (!TryComp(child, out TransformComponent? childXform))
            return false;

        // Children are localised in the parent's own coordinate space, so the
        // world transform has to be projected back into the parent.
        var parentInvMatrix = Matrix3Helpers.CreateInverseTransform(
            parentXform.LocalPosition, parentXform.LocalRotation);

        var childMatrix = Matrix3Helpers.CreateTransform(
            childXform.LocalPosition, childXform.LocalRotation);

        var combined = childMatrix * parentInvMatrix;

        var offset = combined.Translation.Floored();
        var rotation = combined.Rotation();

        _fixtures.Merge(
            parentGrid,
            child,
            offset,
            rotation);

        return true;
    }

    /// <summary>
    /// Removes the planet's own scenery from the area the POI takes over.
    /// The planet generates rocks, lava and flora on its surface tiles, and
    /// those would otherwise end up embedded in the POI's walls.
    /// </summary>
    private void ClearSurfaceEntities(EntityUid mapUid, Box2 bounds)
    {
        if (!TryComp<MapGridComponent>(mapUid, out var grid))
            return;

        var min = _map.TileIndicesFor(mapUid, grid, new EntityCoordinates(mapUid, bounds.BottomLeft));
        var max = _map.TileIndicesFor(mapUid, grid, new EntityCoordinates(mapUid, bounds.TopRight));

        for (var x = min.X; x <= max.X; x++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                var indices = new Vector2i(x, y);
                var anchored = _map.GetAnchoredEntities(mapUid, grid, indices);
                while (anchored.MoveNext(out var entity))
                {
                    if (!TryComp<MetaDataComponent>(entity, out var meta))
                        continue;

                    var id = meta.EntityPrototype?.ID;
                    if (id == "FloorLavaEntity" ||
                        id?.StartsWith("AsteroidRock", StringComparison.Ordinal) == true ||
                        id?.StartsWith("WallRock", StringComparison.Ordinal) == true ||
                        id?.StartsWith("FloraRock", StringComparison.Ordinal) == true)
                    {
                        QueueDel(entity);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Removes the surface tiles that the given POI covers, so the POI's own
    /// tiles replace them instead of overlapping.
    /// </summary>
    private void ClearSurfaceTiles(
        MapGridComponent surfaceGrid,
        MapGridComponent poiGrid,
        Vector2i offsetTile,
        Angle rotation)
    {
        var matrix = Matrix3Helpers.CreateTransform(offsetTile, rotation);

        var cleared = 0;
        var empty = new Tile(0);

        foreach (var tileRef in _map.GetAllTiles(poiGrid.Owner, poiGrid))
        {
            var mergedTile = Vector2.Transform(
                new Vector2(tileRef.GridIndices.X, tileRef.GridIndices.Y) + surfaceGrid.TileSizeHalfVector,
                matrix);

            var indices = mergedTile.Floored();
            if (!_map.TryGetTile(surfaceGrid, indices, out _))
                continue;

            _map.SetTile(surfaceGrid.Owner, surfaceGrid, indices, empty);
            cleared++;
        }

        if (cleared > 0)
            Log.Debug($"Cleared {cleared} surface tiles for Lavaland POI integration.");
    }
}
