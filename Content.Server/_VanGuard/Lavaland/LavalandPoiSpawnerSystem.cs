using System.Linq;
using System.Numerics;
using Content.Server.Fax;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Content.Shared.Fax.Components;
using Content.Shared.Paper;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Server.Parallax;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.Localization;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Places a small set of Lavaland POIs away from the outpost and prints their
/// coordinates on every fax on the planet. Fax delivery itself is delegated to
/// the existing FaxSystem.
/// </summary>
public sealed partial class LavalandPoiSpawnerSystem : EntitySystem
{
    [Dependency] private FaxSystem _fax = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private SharedShuttleSystem _shuttle = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private LavalandFeatureSystem _features = default!;
    [Dependency] private LavalandUniqueStructureSystem _unique = default!;

    public void SpawnPois(Entity<LavalandPoiSpawnerComponent> spawner, EntityUid mapUid, EntityUid outpost)
    {
        if (spawner.Comp.Prototypes.Count == 0 || !TryComp<MapGridComponent>(mapUid, out var mapGrid))
            return;

        var origin = Transform(outpost).WorldPosition;
        var positions = new List<LavalandPoi>();

        // A tendril is only created on a tile that the biome will actually
        // generate as safe basalt and that currently has no lava, chasm or
        // solid obstruction. If no such tile can be found the POI is skipped
        // entirely (and no fax entry is created for it).
        foreach (var prototype in spawner.Comp.Prototypes)
        {
            for (var attempt = 0; attempt < 120; attempt++)
            {
                var angle = _random.NextFloat(0f, MathF.PI * 2f);
                var distance = _random.NextFloat(spawner.Comp.MinDistance, spawner.Comp.MaxDistance);
                var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
                var candidate = new EntityCoordinates(mapUid, origin + offset);
                var indices = _map.TileIndicesFor(mapUid, mapGrid, candidate);

                if (!_features.TryGetValidSpawn(mapUid, mapGrid, indices, out var coordinates, out var tile))
                    continue;

                var chosenOffset = _transform.ToWorldPosition(coordinates) - origin;
                if (positions.Any(existing =>
                        (existing.Offset - chosenOffset).LengthSquared() <
                        spawner.Comp.ClearRadius * spawner.Comp.ClearRadius))
                {
                    continue;
                }

                _features.PrepareSpawnTile(mapUid, mapGrid, indices, tile);
                Spawn(prototype, coordinates);
                positions.Add(new LavalandPoi(chosenOffset, "lavaland-poi-tendril", false));
                break;
            }
        }

        foreach (var dungeon in spawner.Comp.Dungeons)
        {
            var angle = _random.NextFloat(0f, MathF.PI * 2f);
            var distance = _random.NextFloat(spawner.Comp.MinDistance, spawner.Comp.MaxDistance);
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            var mapId = Transform(mapUid).MapID;
            if (!_loader.TryLoadGrid(mapId, dungeon, out var grid, new DeserializationOptions { InitializeMaps = true }) ||
                grid is null)
            {
                continue;
            }

            // Reserve the generated area before moving the dungeon into place so
            // biome chunks cannot grow through its interior.
            if (TryComp<MapGridComponent>(grid.Value.Owner, out var dungeonGrid))
            {
                var bounds = dungeonGrid.LocalAABB.Translated(origin + offset);
                _biome.ReserveTiles(mapUid, bounds, new List<(Vector2i, Tile)>());
            }

            _transform.SetLocalPosition(grid.Value.Owner, origin + offset, Transform(grid.Value.Owner));
            _transform.SetLocalRotation(grid.Value.Owner, Angle.Zero, Transform(grid.Value.Owner));

            // Dungeon maps are authored as shuttles. Turn them into static map
            // geometry so shuttle physics cannot apply angular velocity to them.
            RemComp<ShuttleComponent>(grid.Value.Owner);
            if (TryComp<PhysicsComponent>(grid.Value.Owner, out var physics))
            {
                _physics.SetBodyType(grid.Value.Owner, BodyType.Static, body: physics);
                _physics.SetAngularVelocity(grid.Value.Owner, 0f, body: physics);
            }
            var gridName = GetDungeonName(dungeon);
            _meta.SetEntityName(grid.Value.Owner, Loc.GetString(gridName));
            _shuttle.AddIFFFlag(grid.Value.Owner, IFFFlags.Hide);
            positions.Add(new LavalandPoi(offset, gridName, true));
        }

        if (TryComp<LavalandUniqueStructureSpawnerComponent>(spawner.Owner, out var unique))
            _unique.SpawnStructures((spawner.Owner, unique), mapUid, outpost, positions);

        var faxEntries = positions.Where(poi => poi.InFax).ToList();
        if (!spawner.Comp.SendFax || faxEntries.Count == 0)
            return;

        var content = Loc.GetString("lavaland-poi-fax-header") + "\n\n" +
                      string.Join("\n", faxEntries.Select(poi =>
                          Loc.GetString("lavaland-poi-fax-line",
                              ("x", (int) MathF.Round(poi.Offset.X)),
                              ("y", (int) MathF.Round(poi.Offset.Y)))));
        var printout = new FaxPrintout(
            content,
            Loc.GetString("lavaland-poi-fax-name"),
            null,
            null,
            "paper_stamp-centcom",
            new List<StampDisplayInfo>
            {
                new() { StampedName = Loc.GetString("lavaland-poi-fax-stamp"), StampedColor = Color.FromHex("#0766a5ff") },
            });

        Timer.Spawn(TimeSpan.FromSeconds(spawner.Comp.FaxDelay), () =>
        {
            var query = EntityQueryEnumerator<FaxMachineComponent, TransformComponent>();
            while (query.MoveNext(out var faxUid, out var fax, out var xform))
            {
                if (xform.MapID == Transform(mapUid).MapID)
                    _fax.Receive(faxUid, printout, null, fax);
            }
        });
    }

    private static LocId GetDungeonName(ResPath dungeon)
    {
        var path = dungeon.ToString();
        if (path.Contains("temple"))
            return "lavaland-dungeon-temple";
        if (path.Contains("lava_lake_village"))
            return "lavaland-dungeon-lava-lake";
        if (path.Contains("cave_murder"))
            return "lavaland-dungeon-cave-murder";
        if (path.Contains("clown_room"))
            return "lavaland-dungeon-clown-room";
        if (path.Contains("grasslanddome"))
            return "lavaland-dungeon-grassland-dome";
        if (path.Contains("lava_river"))
            return "lavaland-dungeon-lava-river";
        if (path.Contains("mineshaft"))
            return "lavaland-dungeon-mineshaft";
        if (path.Contains("snowydome"))
            return "lavaland-dungeon-snowy-dome";
        return "lavaland-poi-dungeon";
    }
}
