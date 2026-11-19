using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace VanGuard.Content.VanGuard.Tests.Tests._VanGuard.Lavaland;

/// <summary>
/// Diagnostic experiment: load the very same outpost map file two ways and compare
/// how the grid's children end up parented/gridded.
///
///   A) "production" - a map that already exists and is already map-initialized,
///      then TryLoadGrid(mapId, file) which sets MapLoadOptions.MergeMap = mapId.
///   B) "isolated"   - a freshly created map that is already map-initialized,
///      then TryLoadGrid(file) which creates its own map and merges onto that one.
///
/// Both variants use identical options (InitializeMaps = true) and identical file.
/// The only difference is *which* map the grid is merged onto: an existing one that
/// already went through MapInit, versus a brand new one.
/// </summary>
[TestFixture]
public sealed class LavalandMergeMapExperiment : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private const string OutpostPath = "/Maps/_VanGuard/Lavaland/mining_outpost.yml";

    /// <summary>Entities we care about, identified by prototype plus the local position
    /// they have in the YAML file (so we can spot a mis-parented survivor).</summary>
    private static readonly (string Proto, Vector2 Pos)[] Targets =
    {
        ("LockerMinerFilled", new Vector2(-23.5f, -4.5f)),
        ("LockerMinerFilled", new Vector2(-24.5f, -4.5f)),
        ("LockerMinerFilled", new Vector2(-25.5f, -4.5f)),
        ("LockerMinerFilled", new Vector2(-26.5f, -4.5f)),
        ("LockerMinerFilled", new Vector2(-27.5f, -4.5f)),
        ("LockerMinerFilled", new Vector2(-21.5f, 4.5f)),
        ("LockerMinerFilled", new Vector2(-18.5f, 3.5f)),
        ("LockerMinerFilled", new Vector2(-18.5f, 4.5f)),
        ("LockerMinerFilled", new Vector2(-27.5f, -2.5f)),
        ("ClosetEmergencyFilledRandom", new Vector2(-19.5f, -11.5f)),
        ("ClosetEmergencyFilledRandom", new Vector2(11.5f, 9.5f)),
        ("OxygenCanister", new Vector2(-14.5f, 5.5f)),
        ("Pickaxe", new Vector2(-17.495617f, -12.417178f)),
        ("Pickaxe", new Vector2(-17.526867f, -12.526553f)),
        ("Pickaxe", new Vector2(12.4728f, 11.584418f)),
        ("Pickaxe", new Vector2(12.613425f, 11.412543f)),
        ("SpaceHeater", new Vector2(7.5f, 7.5f)),
    };

    [Test]
    public async Task CompareMergeMapAgainstIsolatedLoad()
    {
        var report = new System.Text.StringBuilder();

        for (var round = 0; round < 3; round++)
        {
            // ---- Variant A: production style. The map already exists and is already
            //      map-initialized, exactly like LoadMapRuleSystem does before the
            //      Lavaland rule runs.
            await Server.WaitPost(() =>
            {
                var mapSystem = SEntMan.System<SharedMapSystem>();
                mapSystem.CreateMap(out var mapAId, runMapInit: true);

                var loaderA = SEntMan.System<Robust.Shared.EntitySerialization.Systems.MapLoaderSystem>();
                var okA = loaderA.TryLoadGrid(
                    mapAId,
                    new ResPath(OutpostPath),
                    out var gridA,
                    new Robust.Shared.EntitySerialization.DeserializationOptions { InitializeMaps = true });

                report.AppendLine($"=== ROUND {round} VARIANT A (merge onto existing map, map has NO MapGridComponent) ===");
                report.AppendLine($"  loadOk={okA} grid={(gridA is { } ga ? ga.Owner.ToString() : "none")}");

                if (gridA is { } grid)
                {
                    AppendGridState(report, "  A", grid.Owner, mapAId);
                }
            });

            // ---- Variant B: isolated. A brand new map is created and map-initialized
            //      first, then the same file is loaded onto it. TryLoadGrid(file) makes
            //      its own map and merges onto that.
            await Server.WaitPost(() =>
            {
                var loaderB = SEntMan.System<Robust.Shared.EntitySerialization.Systems.MapLoaderSystem>();
                var okB = loaderB.TryLoadGrid(
                    new ResPath(OutpostPath),
                    out var mapB,
                    out var gridB,
                    new Robust.Shared.EntitySerialization.DeserializationOptions { InitializeMaps = true });

                report.AppendLine($"=== ROUND {round} VARIANT B (merge onto own fresh map) ===");
                report.AppendLine($"  loadOk={okB} grid={(gridB is { } gb ? gb.Owner.ToString() : "none")}");

                if (gridB is { } grid)
                {
                    AppendGridState(report, "  B", grid.Owner, mapB?.Comp.MapId);
                }
            });

            // ---- Variant C: production-like WITH a planet grid already on the map.
            //      LavalandOutpostRuleSystem calls BiomeSystem.EnsurePlanet before
            //      loading the outpost, and EnsurePlanet adds a MapGridComponent to the
            //      map entity itself. That makes the map itself a grid.
            await Server.WaitPost(() =>
            {
                var mapSystem = SEntMan.System<SharedMapSystem>();
                mapSystem.CreateMap(out var mapCId, runMapInit: true);

                // Give the map its own MapGridComponent, exactly like EnsurePlanet does.
                if (mapSystem.TryGetMap(mapCId, out var mapCEntity))
                {
                    SEntMan.AddComponent<MapGridComponent>(mapCEntity.Value);
                }

                var loaderC = SEntMan.System<Robust.Shared.EntitySerialization.Systems.MapLoaderSystem>();
                var okC = loaderC.TryLoadGrid(
                    mapCId,
                    new ResPath(OutpostPath),
                    out var gridC,
                    new Robust.Shared.EntitySerialization.DeserializationOptions { InitializeMaps = true });

                report.AppendLine($"=== ROUND {round} VARIANT C (merge onto map that ALREADY has MapGridComponent) ===");
                report.AppendLine($"  loadOk={okC} grid={(gridC is { } gc ? gc.Owner.ToString() : "none")}");

                if (gridC is { } grid)
                {
                    AppendGridState(report, "  C", grid.Owner, mapCId);
                }
            });

            // ---- Variant D: same as C, but the planet grid is added AFTER the outpost
            //      was loaded. Isolates ordering as the trigger.
            await Server.WaitPost(() =>
            {
                var mapSystem = SEntMan.System<SharedMapSystem>();
                mapSystem.CreateMap(out var mapDId, runMapInit: true);

                var loaderD = SEntMan.System<Robust.Shared.EntitySerialization.Systems.MapLoaderSystem>();
                var okD = loaderD.TryLoadGrid(
                    mapDId,
                    new ResPath(OutpostPath),
                    out var gridD,
                    new Robust.Shared.EntitySerialization.DeserializationOptions { InitializeMaps = true });

                report.AppendLine($"=== ROUND {round} VARIANT D (outpost first, planet grid added after) ===");
                report.AppendLine($"  loadOk={okD} grid={(gridD is { } gd ? gd.Owner.ToString() : "none")}");

                if (gridD is { } grid)
                {
                    AppendGridState(report, "  D", grid.Owner, mapDId);
                }
            });
        }

        var text = report.ToString();
        TestContext.Out.WriteLine(text);
        try { System.IO.File.AppendAllText("/tmp/lavaland-mergeexperiment.log", text); } catch { }
    }

    /// <summary>
    /// Dumps how the entities on a grid ended up: how many children the grid has, how many
    /// of them report the grid as their GridUid, and the full state of every target entity.
    /// </summary>
    private void AppendGridState(System.Text.StringBuilder report, string prefix, EntityUid gridUid, MapId? mapUid)
    {
        var total = 0;
        var gridUidMatches = 0;
        var parentIsGrid = 0;
        var anchored = 0;

        var children = new Dictionary<string, List<(EntityUid uid, EntityUid? parent, EntityUid? grid, Vector2 local, Vector2 world, bool anch)>>();

        var q = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (q.MoveNext(out var uid, out var meta, out var xform))
        {
            var id = meta.EntityPrototype?.ID;
            if (id is null)
                continue;

            var onGrid = xform.GridUid == gridUid;

            // Everything parented to the grid, plus anything that claims the grid.
            if (xform.ParentUid != gridUid && !onGrid)
                continue;

            total++;
            if (onGrid)
                gridUidMatches++;
            if (xform.ParentUid == gridUid)
                parentIsGrid++;
            if (xform.Anchored)
                anchored++;

            if (!children.TryGetValue(id, out var list))
            {
                list = new List<(EntityUid, EntityUid?, EntityUid?, Vector2, Vector2, bool)>();
                children[id] = list;
            }

            list.Add((uid, xform.ParentUid, xform.GridUid, xform.LocalPosition, xform.WorldPosition, xform.Anchored));
        }

        report.AppendLine($"{prefix} grid={gridUid} childrenOnGrid={total} " +
                           $"gridUidMatches={gridUidMatches} parentIsGrid={parentIsGrid} anchored={anchored}");

        // The grid's own GridUid tells us whether the grid itself thinks it lives on
        // the map's grid (the planet grid) or on itself.
        if (SEntMan.TryGetComponent<TransformComponent>(gridUid, out var gridXform))
        {
            report.AppendLine($"{prefix}   GRID-OWN: gridUid={gridXform.GridUid} " +
                              $"(isSelf={gridXform.GridUid == gridUid}) " +
                              $"parent={(gridXform.ParentUid is { } p ? p.ToString() : "null")} " +
                              $"(isMap={mapUid is { } mid && gridXform.MapID == mid}) " +
                              $"mapId={gridXform.MapID}");
        }

        foreach (var (proto, want) in Targets)
        {
            if (!children.TryGetValue(proto, out var list))
            {
                report.AppendLine($"{prefix}   {proto}@{want}: ABSENT");
                continue;
            }

            var best = list
                .Where(e => (e.local - want).LengthSquared() < 0.35f * 0.35f)
                .ToList();

            if (best.Count == 0)
            {
                // Fall back to the closest one so we can still see what happened to it.
                var closest = list.OrderBy(e => (e.local - want).LengthSquared()).First();
                report.AppendLine($"{prefix}   {proto}@{want}: NO EXACT MATCH " +
                                  $"(closest uid={closest.uid} local=({closest.local.X:F3},{closest.local.Y:F3}) " +
                                  $"parent={(closest.parent is { } p ? p.ToString() : "null")} " +
                                  $"grid={(closest.grid is { } g ? g.ToString() : "null")} " +
                                  $"world=({closest.world.X:F3},{closest.world.Y:F3}) anchored={closest.anch})");
                continue;
            }

            foreach (var e in best)
            {
                report.AppendLine($"{prefix}   {proto}@{want}: uid={e.uid} " +
                                  $"parent={(e.parent is { } p ? p.ToString() : "null")} " +
                                  $"(isGrid={e.parent == gridUid}) " +
                                  $"grid={(e.grid is { } g ? g.ToString() : "null")} " +
                                  $"(matchesGrid={e.grid == gridUid}) " +
                                  $"local=({e.local.X:F3},{e.local.Y:F3}) " +
                                  $"world=({e.world.X:F3},{e.world.Y:F3}) anchored={e.anch}");
            }
        }
    }
}
