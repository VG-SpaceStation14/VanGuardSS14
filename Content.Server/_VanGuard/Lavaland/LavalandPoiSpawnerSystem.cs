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
        var outpostGrid = Comp<MapGridComponent>(outpost);
        var outpostBounds = outpostGrid.LocalAABB
            .Translated(Transform(outpost).WorldPosition)
            .Enlarged(spawner.Comp.ClearRadius);
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
                if (outpostBounds.Contains(origin + chosenOffset))
                    continue;
                if (positions.Any(existing =>
                        (existing.Offset - chosenOffset).LengthSquared() <
                        spawner.Comp.ClearRadius * spawner.Comp.ClearRadius))
                {
                    continue;
                }

                _features.PrepareSpawnTile(mapUid, mapGrid, indices, tile);
                Spawn(prototype, coordinates);
                positions.Add(new LavalandPoi(chosenOffset, "lavaland-poi-tendril", "lavaland-poi-tendril-fax", true, true));
                break;
            }
        }

        var occupied = new List<Box2> { outpostBounds };
        foreach (var dungeon in spawner.Comp.Dungeons)
        {
            var placed = false;
            for (var attempt = 0; attempt < 120; attempt++)
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

                if (!TryComp<MapGridComponent>(grid.Value.Owner, out var dungeonGrid))
                {
                    QueueDel(grid.Value.Owner);
                    continue;
                }

                var bounds = dungeonGrid.LocalAABB.Translated(origin + offset);
                var placementBounds = bounds.Enlarged(4f);
                if (occupied.Any(existing => existing.Intersects(placementBounds)))
                {
                    QueueDel(grid.Value.Owner);
                    continue;
                }

                // Reserve the generated area before moving the dungeon into place so
                // biome chunks cannot grow through its interior.
                _biome.ReserveTiles(mapUid, bounds, new List<(Vector2i, Tile)>());

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
                occupied.Add(placementBounds);
                positions.Add(new LavalandPoi(offset, gridName, GetDungeonFaxName(dungeon), true, false));
                placed = true;
                break;
            }

            if (!placed)
                Log.Warning($"Could not place Lavaland dungeon {dungeon}.");
        }

        if (TryComp<LavalandUniqueStructureSpawnerComponent>(spawner.Owner, out var unique))
            _unique.SpawnStructures((spawner.Owner, unique), mapUid, outpost, positions, occupied);

        var faxEntries = positions.Where(poi => poi.InFax).ToList();
        if (!spawner.Comp.SendFax || faxEntries.Count == 0)
            return;

        var dungeons = faxEntries.Where(poi => !poi.IsTendril).ToList();
        var tendrils = faxEntries.Where(poi => poi.IsTendril).ToList();
        var sections = new List<string>
        {
            Loc.GetString("lavaland-poi-fax-header"),
            Loc.GetString("lavaland-poi-fax-form"),
            Loc.GetString("lavaland-poi-fax-classification"),
            Loc.GetString("lavaland-poi-fax-zone"),
            Loc.GetString("lavaland-poi-fax-rule"),
        };

        if (dungeons.Count > 0)
        {
            sections.Add(string.Empty);
            sections.Add(Loc.GetString("lavaland-poi-fax-dungeons"));
            sections.AddRange(dungeons.Select((poi, index) =>
                Loc.GetString("lavaland-poi-fax-dungeon-entry",
                    ("index", index + 1),
                    ("name", Loc.GetString(poi.FaxName)),
                    ("x", (int) MathF.Round(poi.Offset.X)),
                    ("y", (int) MathF.Round(poi.Offset.Y)))));
        }

        if (tendrils.Count > 0)
        {
            sections.Add(string.Empty);
            sections.Add(Loc.GetString("lavaland-poi-fax-tendrils"));
            sections.AddRange(tendrils.Select((poi, index) =>
                Loc.GetString("lavaland-poi-fax-tendril-entry",
                    ("index", index + 1),
                    ("name", Loc.GetString(poi.FaxName)),
                    ("x", (int) MathF.Round(poi.Offset.X)),
                    ("y", (int) MathF.Round(poi.Offset.Y)))));
        }

        sections.Add(string.Empty);
        sections.Add(Loc.GetString("lavaland-poi-fax-total", ("count", faxEntries.Count)));
        sections.Add(Loc.GetString("lavaland-poi-fax-footer"));
        var content = string.Join("\n", sections);
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
            if (!Exists(mapUid) || !TryComp(mapUid, out TransformComponent? mapXform))
                return;

            var mapId = mapXform.MapID;
            var query = EntityQueryEnumerator<FaxMachineComponent, TransformComponent>();
            while (query.MoveNext(out var faxUid, out var fax, out var xform))
            {
                if (xform.MapID == mapId)
                    _fax.Receive(faxUid, printout, null, fax);
            }
        });
    }

    private static LocId GetDungeonFaxName(ResPath dungeon)
    {
        var path = dungeon.ToString();
        if (path.Contains("temple"))
            return "lavaland-fax-object-monument";
        if (path.Contains("lava_lake_village"))
            return "lavaland-fax-object-lake-village";
        if (path.Contains("cave_murder"))
            return "lavaland-fax-object-cave";
        if (path.Contains("clown_room"))
            return "lavaland-fax-object-bright-room";
        if (path.Contains("grasslanddome"))
            return "lavaland-fax-object-green-dome";
        if (path.Contains("lava_river"))
            return "lavaland-fax-object-lava-river";
        if (path.Contains("mineshaft"))
            return "lavaland-fax-object-mineshaft";
        if (path.Contains("snowydome"))
            return "lavaland-fax-object-cold-dome";
        return "lavaland-fax-object-unknown";
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
