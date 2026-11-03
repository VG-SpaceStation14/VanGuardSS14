using Content.Server.Parallax;
using Content.Shared.Chasm;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Handles biome feature markers (ore deposits, tendrils and similar cave
/// decorations) and provides the shared tile validation used by every Lavaland
/// spawner. A feature is only ever created after its target tile passed both
/// the biome prediction and the currently-materialized entity checks.
/// </summary>
public sealed partial class LavalandFeatureSystem : EntitySystem
{
    private const int BiomeChunkSize = 8;

    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private BiomeSystem _biome = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LavalandOreDepositComponent, MapInitEvent>(OnOreInit);
        SubscribeLocalEvent<LavalandBiomeFeatureComponent, MapInitEvent>(OnFeatureInit);
    }

    private void OnOreInit(Entity<LavalandOreDepositComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Ores.Count == 0)
            return;

        Spawn(_random.Pick(ent.Comp.Ores), Transform(ent.Owner).Coordinates);
        QueueDel(ent.Owner);
    }

    private void OnFeatureInit(Entity<LavalandBiomeFeatureComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Options.Count == 0)
            return;

        var xform = Transform(ent.Owner);
        if (xform.MapUid is not { } mapUid || !TryComp<MapGridComponent>(mapUid, out var grid))
        {
            QueueDel(ent.Owner);
            return;
        }

        var origin = _map.TileIndicesFor(mapUid, grid, xform.Coordinates);

        for (var attempt = 0; attempt < 12; attempt++)
        {
            // Pick a whole tile, never an unsnapped point: the tendril is
            // anchored to the tile it was generated for.
            var offset = new Vector2i(_random.Next(-2, 3), _random.Next(-2, 3));
            var indices = origin + offset;

            if (!TryGetValidSpawn(mapUid, grid, indices, out var chosen, out var tile))
                continue;

            PrepareSpawnTile(mapUid, grid, indices, tile);
            Spawn(_random.Pick(ent.Comp.Options), chosen);
            QueueDel(ent.Owner);
            return;
        }

        // No valid tile after all attempts: create nothing.
        QueueDel(ent.Owner);
    }

    /// <summary>
    /// Checks whether the given tile is a safe, non-lava and non-chasm floor.
    /// Works for ungenerated biome tiles by asking the biome for the tile and
    /// top entity it would create there.
    /// </summary>
    public bool TryGetValidSpawn(
        EntityUid mapUid,
        MapGridComponent grid,
        Vector2i indices,
        out EntityCoordinates coordinates,
        out Tile tile,
        EntityUid? ignore = null)
    {
        coordinates = _map.GridTileToLocal(mapUid, grid, indices);
        tile = default;

        if (!_biome.TryGetBiomeTile(mapUid, grid, indices, out var biomeTile) || biomeTile.Value.IsEmpty)
            return false;

        tile = biomeTile.Value;

        // When validating an already-spawned feature its own fixture is a
        // legitimate occupant of the tile, so only the anchored-entity pass
        // below (with the ignore argument) should judge it.
        if (ignore == null &&
            _turf.TryGetTileRef(coordinates, out var tileRef) &&
            _turf.IsTileBlocked(tileRef.Value, CollisionGroup.MobMask))
        {
            return false;
        }

        if (TryComp<BiomeComponent>(mapUid, out var biome) &&
            _biome.TryGetEntity(indices, biome, (mapUid, grid), out var prototype) &&
            IsInvalidBiomeEntity(prototype))
        {
            return false;
        }

        var anchored = _map.GetAnchoredEntities(mapUid, grid, indices);
        while (anchored.MoveNext(out var entity))
        {
            if (entity == ignore)
                continue;

            if (HasComp<ChasmComponent>(entity))
                return false;

            if (TryComp<MetaDataComponent>(entity, out var meta) &&
                meta.EntityPrototype?.ID == "FloorLavaEntity")
            {
                return false;
            }

            // Solid rocks and other collidable objects are not valid tendril
            // ground either. Decorative basalt tiles have no physics.
            if (TryComp<PhysicsComponent>(entity, out var physics) && physics.CanCollide)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Materializes the predicted floor tile and marks it as modified so that
    /// later biome marker layers (for example chasm clusters) cannot overwrite
    /// the tile occupied by the new feature.
    /// </summary>
    public void PrepareSpawnTile(EntityUid mapUid, MapGridComponent grid, Vector2i indices, Tile tile)
    {
        if (!_map.TryGetTile(grid, indices, out var existing) || existing.IsEmpty)
            _map.SetTile(mapUid, grid, indices, tile);

        if (!TryComp<BiomeComponent>(mapUid, out var biome))
            return;

        var chunk = SharedMapSystem.GetChunkIndices(indices, BiomeChunkSize) * BiomeChunkSize;
        biome.ModifiedTiles.GetOrNew(chunk).Add(indices);
    }

    private static bool IsInvalidBiomeEntity(string prototype)
    {
        return prototype == "FloorLavaEntity"
               || prototype == "FloorChasmEntity"
               || prototype.StartsWith("WallRock", StringComparison.Ordinal)
               || prototype.StartsWith("FloraRock", StringComparison.Ordinal);
    }
}
