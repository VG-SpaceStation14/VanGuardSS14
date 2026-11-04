using System.Linq;
using System.Numerics;
using Content.Server.Parallax;
using Content.Shared.Chasm;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Random;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Places guaranteed unique structures (currently the Blood Miner arena) on
/// Lavaland. Each structure picks one of its variants and is placed at most
/// once per map, even if this system is called again.
/// </summary>
public sealed partial class LavalandUniqueStructureSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private SharedShuttleSystem _shuttle = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;

    private readonly HashSet<(MapId Map, string Id)> _spawned = new();

    public void SpawnStructures(
        Entity<LavalandUniqueStructureSpawnerComponent> spawner,
        EntityUid mapUid,
        EntityUid outpost,
        List<LavalandPoi> pois,
        List<Box2> occupied)
    {
        if (spawner.Comp.Structures.Count == 0)
            return;

        var mapId = Transform(mapUid).MapID;
        var origin = Transform(outpost).WorldPosition;

        foreach (var structure in spawner.Comp.Structures)
        {
            if (!_spawned.Add((mapId, structure.Id)))
                continue;

            if (!TrySpawn(structure, mapUid, mapId, origin, pois, occupied))
            {
                _spawned.Remove((mapId, structure.Id));
                Log.Warning($"Could not place unique Lavaland structure {structure.Id}.");
            }
        }
    }

    private bool TrySpawn(
        LavalandUniqueStructure structure,
        EntityUid mapUid,
        MapId mapId,
        Vector2 origin,
        List<LavalandPoi> pois,
        List<Box2> occupied)
    {
        if (structure.Variants.Count == 0 || !TryComp<MapGridComponent>(mapUid, out var mapGrid))
            return false;

        var variants = structure.Variants.ToList();

        // One Lavaland map exists per round, so pick the layout randomly for
        // this world and fall back to the other layouts only if it fails.
        var startVariant = _random.Next(variants.Count);

        for (var attempt = 0; attempt < 120; attempt++)
        {
            var angle = _random.NextFloat(0f, MathF.PI * 2f);
            var distance = _random.NextFloat(structure.MinDistance, structure.MaxDistance);
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;

            if (pois.Any(poi =>
                    (poi.Offset - offset).LengthSquared() <
                    structure.ClearRadius * structure.ClearRadius))
            {
                continue;
            }

            for (var i = 0; i < variants.Count; i++)
            {
                var variant = variants[(startVariant + i) % variants.Count];
                if (!_loader.TryLoadGrid(mapId, variant, out var grid,
                        new DeserializationOptions { InitializeMaps = false }) ||
                    grid is null)
                {
                    continue;
                }

                if (!TryComp<MapGridComponent>(grid.Value.Owner, out var gridComp))
                {
                    DeleteGridTree(grid.Value.Owner);
                    continue;
                }

                // Clear the planet-side footprint before placing the structure.
                // This removes generated lava/chasms and rock entities that
                // would otherwise end up inside the base or arena.
                var bounds = gridComp.LocalAABB.Translated(origin + offset);
                var placementBounds = bounds.Enlarged(4f);
                if (occupied.Any(existing => existing.Intersects(placementBounds)))
                {
                    DeleteGridTree(grid.Value.Owner);
                    continue;
                }

                ClearArea(mapUid, mapGrid, bounds);

                // Reserve before moving so the biome cannot grow through the
                // structure interior.
                _biome.ReserveTiles(mapUid, bounds, new List<(Vector2i, Tile)>());

                // Centre the authored grid on the chosen point.
                _transform.SetLocalPosition(
                    grid.Value.Owner,
                    origin + offset - gridComp.LocalAABB.Center,
                    Transform(grid.Value.Owner));
                _transform.SetLocalRotation(grid.Value.Owner, Angle.Zero, Transform(grid.Value.Owner));

                // Dungeon maps are authored as shuttles; convert them to static
                // planet geometry.
                RemComp<ShuttleComponent>(grid.Value.Owner);
                if (TryComp<PhysicsComponent>(grid.Value.Owner, out var physics))
                {
                    _physics.SetBodyType(grid.Value.Owner, BodyType.Static, body: physics);
                    _physics.SetAngularVelocity(grid.Value.Owner, 0f, body: physics);
                }

                _meta.SetEntityName(grid.Value.Owner, Loc.GetString(structure.Name));
                _shuttle.AddIFFFlag(grid.Value.Owner, IFFFlags.Hide);

                // Initialize only after the grid is in its final position. This
                // prevents spawners from firing at the map origin while a
                // candidate layout is still being validated.
                InitializeGridTree(grid.Value.Owner);
                var spawnedGrid = grid.Value.Owner;
                Timer.Spawn(TimeSpan.Zero, () =>
                {
                    if (Exists(spawnedGrid))
                        EnsureGuaranteedSpawns(spawnedGrid, structure.GuaranteedSpawns);
                });
                occupied.Add(placementBounds);
                pois.Add(new LavalandPoi(offset, structure.Name, structure.FaxName, true, false));
                return true;
            }
        }

        return false;
    }

    private void EnsureGuaranteedSpawns(EntityUid grid, List<EntProtoId> prototypes)
    {
        if (prototypes.Count == 0)
            return;

        foreach (var prototype in prototypes)
        {
            var found = false;
            var query = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out _, out var meta, out var xform))
            {
                if (xform.GridUid == grid && meta.EntityPrototype?.ID == prototype.Id)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                Spawn(prototype, Transform(grid).Coordinates);
        }
    }

    private void InitializeGridTree(EntityUid grid)
    {
        var pending = new List<EntityUid> { grid };
        for (var i = 0; i < pending.Count; i++)
        {
            var uid = pending[i];
            if (!TryComp(uid, out MetaDataComponent? meta))
                continue;

            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
                pending.Add(child);

            if (meta.EntityLifeStage < EntityLifeStage.MapInitialized)
                EntityManager.RunMapInit(uid, meta);
        }
    }

    private void DeleteGridTree(EntityUid grid)
    {
        var children = new List<EntityUid>();
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (uid == grid)
                continue;

            if (xform.GridUid == grid || xform.ParentUid == grid)
                children.Add(uid);
        }

        foreach (var child in children)
        {
            if (Exists(child))
                QueueDel(child);
        }

        QueueDel(grid);
    }

    private void ClearArea(EntityUid mapUid, MapGridComponent grid, Box2 bounds)
    {
        var min = _map.TileIndicesFor(mapUid, grid, new EntityCoordinates(mapUid, bounds.BottomLeft));
        var max = _map.TileIndicesFor(mapUid, grid, new EntityCoordinates(mapUid, bounds.TopRight));
        var basalt = new Tile(_tiles["FloorBasalt"].TileId);

        for (var x = min.X; x <= max.X; x++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                var indices = new Vector2i(x, y);
                _map.SetTile(mapUid, grid, indices, basalt);

                var anchored = _map.GetAnchoredEntities(mapUid, grid, indices);
                while (anchored.MoveNext(out var entity))
                {
                    if (HasComp<ChasmComponent>(entity))
                    {
                        QueueDel(entity);
                        continue;
                    }

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

        // GetAnchoredEntities can miss entities whose fixture is not yet
        // anchored. Sweep the planet map directly as a final safety net.
        var mapId = Transform(mapUid).MapID;
        var sweep = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (sweep.MoveNext(out var entity, out var meta, out var xform))
        {
            if (xform.MapID != mapId || xform.GridUid != mapUid || !bounds.Contains(xform.WorldPosition))
                continue;

            var id = meta.EntityPrototype?.ID;
            if (id?.StartsWith("AsteroidRock", StringComparison.Ordinal) == true ||
                id?.StartsWith("WallRock", StringComparison.Ordinal) == true ||
                id?.StartsWith("FloraRock", StringComparison.Ordinal) == true ||
                HasComp<ChasmComponent>(entity))
            {
                QueueDel(entity);
            }
        }
    }
}
