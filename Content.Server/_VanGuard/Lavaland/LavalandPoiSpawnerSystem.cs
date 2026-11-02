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
using Robust.Shared.Random;
using Robust.Shared.Timing;

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

    public void SpawnPois(Entity<LavalandPoiSpawnerComponent> spawner, EntityUid mapUid, EntityUid outpost)
    {
        if (spawner.Comp.Prototypes.Count == 0 || !TryComp<MapGridComponent>(mapUid, out var mapGrid))
            return;

        var origin = Transform(outpost).WorldPosition;
        var positions = new List<Vector2>();
        var attempts = 0;
        while (positions.Count < spawner.Comp.Prototypes.Count && attempts++ < 500)
        {
            var angle = _random.NextFloat(0f, MathF.PI * 2f);
            var distance = _random.NextFloat(spawner.Comp.MinDistance, spawner.Comp.MaxDistance);
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            var coordinates = new EntityCoordinates(mapUid, origin + offset);

            if (positions.Any(existing => (existing - offset).LengthSquared() < spawner.Comp.ClearRadius * spawner.Comp.ClearRadius))
                continue;

            positions.Add(offset);
            Spawn(spawner.Comp.Prototypes[positions.Count - 1], coordinates);
        }

        foreach (var dungeon in spawner.Comp.Dungeons)
        {
            if (positions.Count >= spawner.Comp.Prototypes.Count + spawner.Comp.Dungeons.Count)
                break;

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
            var gridName = dungeon.ToString().Contains("temple")
                ? "lavaland-dungeon-temple"
                : "lavaland-dungeon-lava-lake";
            _meta.SetEntityName(grid.Value.Owner, Loc.GetString(gridName));
            _shuttle.AddIFFFlag(grid.Value.Owner, IFFFlags.Hide);
            positions.Add(offset);
        }

        if (!spawner.Comp.SendFax || positions.Count == 0)
            return;

        var content = Loc.GetString("lavaland-poi-fax-header") + "\n\n" +
                      string.Join("\n", positions.Select(position =>
                          Loc.GetString("lavaland-poi-fax-line", ("x", position.X), ("y", position.Y))));
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
}
