using System.Linq;
using System.Numerics;
using Content.Server.Parallax;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
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

    private readonly HashSet<(MapId Map, string Id)> _spawned = new();

    public void SpawnStructures(
        Entity<LavalandUniqueStructureSpawnerComponent> spawner,
        EntityUid mapUid,
        EntityUid outpost,
        List<LavalandPoi> pois)
    {
        if (spawner.Comp.Structures.Count == 0)
            return;

        var mapId = Transform(mapUid).MapID;
        var origin = Transform(outpost).WorldPosition;

        foreach (var structure in spawner.Comp.Structures)
        {
            if (!_spawned.Add((mapId, structure.Id)))
                continue;

            if (!TrySpawn(structure, mapUid, mapId, origin, pois))
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
        List<LavalandPoi> pois)
    {
        if (structure.Variants.Count == 0 || !TryComp<MapGridComponent>(mapUid, out var mapGrid))
            return false;

        var variants = structure.Variants.ToList();
        _random.Shuffle(variants);

        for (var attempt = 0; attempt < 80; attempt++)
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

            foreach (var variant in variants)
            {
                if (!_loader.TryLoadGrid(mapId, variant, out var grid,
                        new DeserializationOptions { InitializeMaps = true }) ||
                    grid is null)
                {
                    continue;
                }

                if (!TryComp<MapGridComponent>(grid.Value.Owner, out var gridComp))
                {
                    QueueDel(grid.Value.Owner);
                    continue;
                }

                // Reserve before moving so the biome cannot grow through the
                // structure interior.
                var bounds = gridComp.LocalAABB.Translated(origin + offset);
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
                pois.Add(new LavalandPoi(offset, structure.Name, true));
                return true;
            }
        }

        return false;
    }
}
