#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._VanGuard.Lavaland.Components;
using Content.Server.GameTicking;
using Content.Server.Parallax;
using Content.Shared.GameTicking.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Lavaland;

/// <summary>
/// Diagnostic-only test for Lavaland outpost loading (Round 3).
/// Does NOT modify any production code.
///
/// This round checks the outpost loading RESULT directly instead of relying on
/// a global "find nearest entity with this prototype" search, which in Round 2
/// matched entities on unrelated grids.
///
/// For every seed we record:
///   - whether the outpost grid was loaded at all
///     (LavalandOutpostRuleComponent.OutpostGrid being valid);
///   - the OutpostGrid uid, its MapUid and its LocalAABB;
///   - the targeted entities, but ONLY counted on the OutpostGrid itself:
///       * LockerMinerFilled (all of them, with positions)
///       * ClosetEmergencyFilledRandom @ (-19.5, -11.5)
///       * OxygenCanister        @ (-14.5,   5.5)
///       * Pickaxe               @ lower part of the outpost
///   - separately: entities of the same prototypes found on OTHER grids of the
///     same map, reported as "incidental" matches only, never as evidence that
///     the outpost contains them.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class LavalandDiagnosticsTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private static readonly int[] Seeds = { 424242, 1, 2, 3, 7, 13, 100, 4243 };

    private const float PosTolerance = 0.6f;
    private static readonly Vector2 ClosetEmergencyLowerPos = new(-19.5f, -11.5f);
    private static readonly Vector2 OxygenCanisterPos        = new(-14.5f,   5.5f);
    private static readonly Vector2[] LowerPickaxePoses =
    {
        new(-17.495617f, -12.417178f),
        new(-17.526867f, -12.526553f),
    };

    [Test]
    public async Task OutpostLoadResult_And_Contents_On_OutpostGrid_AcrossSeeds()
    {
        var ticker = Server.System<ServerGameTicker>();
        var loader = new EntProtoId("LavalandLoader");

        foreach (var seed in Seeds)
        {
            Entity<GameRuleComponent>? rule = null;
            EntityUid? player = null;
            try
            {
                await Server.WaitPost(() =>
                {
                    rule = ticker.AddGameRule(loader);
                    if (rule is { } added)
                        SEntMan.GetComponent<LavalandOutpostRuleComponent>(added.Owner).Seed = seed;
                });
                await Pair.RunUntilSynced();

                if (rule is not { } added)
                {
                    Report($"[{seed}] rule not added; skipping");
                    continue;
                }

                var ruleUid = added.Owner;
                if (!SEntMan.TryGetComponent<LavalandOutpostRuleComponent>(ruleUid, out var ruleComp)
                    || ruleComp.Map is not { } mapUid
                    || ruleComp.MapId is not { } mapId)
                {
                    Report($"[{seed}] rule missing Map/MapId; skipping");
                    continue;
                }

                // ---- Round 3 point 1: the load result itself.
                var outpostUidNullable = ruleComp.OutpostGrid;
                var outpostValid = outpostUidNullable is { } candidate
                                  && candidate != EntityUid.Invalid
                                  && SEntMan.EntityExists(candidate);
                var lines = new List<string>();
                lines.Add($"  mapUid={mapUid} mapId={mapId}");
                lines.Add($"  OutpostGrid={outpostUidNullable} valid={outpostValid}");

                if (!outpostValid || outpostUidNullable is not { } outpostUid)
                {
                    lines.Add("  >>> OUTPOST GRID WAS NOT LOADED (OutpostGrid invalid) <<<");
                    Report($"[{seed}] {string.Join("\n", lines)}");
                    continue;
                }

                if (!SEntMan.TryGetComponent<MapGridComponent>(outpostUid, out var outpostGrid))
                {
                    lines.Add("  >>> OutpostGrid has no MapGridComponent <<<");
                    Report($"[{seed}] {string.Join("\n", lines)}");
                    continue;
                }

                var outpostXform = SEntMan.GetComponent<TransformComponent>(outpostUid);
                lines.Add($"  OutpostGrid.MapUid={outpostXform.MapID} " +
                          $"LocalPosition=({outpostXform.LocalPosition.X:F3},{outpostXform.LocalPosition.Y:F3}) " +
                          $"Anchored={outpostXform.Anchored}");
                lines.Add($"  OutpostGrid.LocalAABB={outpostGrid.LocalAABB}");

                // ---- Round 3 point 2: targeted entities, strictly on the outpost grid.
                lines.AddRange(DescribeOutpostGrid(mapId, outpostUid));

                // ---- Round 3 point 4: incidental matches on other grids.
                lines.AddRange(DescribeIncidental(mapId, outpostUid));

                // ---- Round 3a: full prototype census of the outpost grid, so two seeds
                //      can be diffed against each other to see exactly what is missing.
                DumpOutpostCensus(seed, outpostUid);
                DumpOutpostLocate(seed, outpostUid);

                Report($"[{seed}] {string.Join("\n", lines)}");

                // ---- Round 4: snapshot the SAME entities again after the map has been
                //      initialized and after biome ticks, so we can tell whether the
                //      entities were already gone at load time or disappeared later.
                var t0 = DescribeOutpostGrid(mapId, outpostUid);
                Report($"[{seed}] T0(after load, before ticks)\n{string.Join("\n", t0)}");

                // Attach a viewer so that biome chunk loading actually runs.
                EntityUid viewer = default;
                await Server.WaitPost(() =>
                {
                    viewer = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(mapUid, Vector2.Zero));
                    Server.PlayerMan.SetAttachedEntity(ServerSession, viewer);
                });

                await Pair.RunTicksSync(30);

                var t1 = DescribeOutpostGrid(mapId, outpostUid);
                Report($"[{seed}] T1(after 30 ticks)\n{string.Join("\n", t1)}");

                // Force a wide biome preload, then more ticks.
                await Server.WaitPost(() =>
                {
                    if (SEntMan.TryGetComponent<BiomeComponent>(mapUid, out var biome))
                        Server.System<BiomeSystem>().Preload(mapUid, biome, new Box2(-160, -160, 160, 160));
                });
                await Pair.RunTicksSync(60);

                var t2 = DescribeOutpostGrid(mapId, outpostUid);
                Report($"[{seed}] T2(after 90 ticks + wide preload)\n{string.Join("\n", t2)}");

                // Per-chunk census with parent/anchored, to see exactly which chunk
                // loses its entities and whether the survivors were re-parented.
                DumpOutpostChunkDetail(seed, outpostUid);
            }
            finally
            {
                await Server.WaitPost(() =>
                {
                    if (ServerSession != null)
                        Server.PlayerMan.SetAttachedEntity(ServerSession, null);
                    if (player is { } p && SEntMan.EntityExists(p))
                        SEntMan.DeleteEntity(p);
                    if (rule is { } r && SEntMan.EntityExists(r.Owner))
                        ticker.EndGameRule((r.Owner, (GameRuleComponent?)r.Comp));
                    if (rule is not null
                        && SEntMan.TryGetComponent<LavalandOutpostRuleComponent>(rule.Value.Owner, out var component)
                        && component.Map is { } map
                        && SEntMan.EntityExists(map))
                    {
                        SEntMan.DeleteEntity(map);
                    }
                });
                await Pair.RunUntilSynced();
            }
        }
    }

    // ---------------------------------------------------------------------
    // Entities restricted to the outpost grid.
    // ---------------------------------------------------------------------

    private List<string> DescribeOutpostGrid(MapId mapId, EntityUid outpostUid)
    {
        var lines = new List<string>();

        var miners = new List<(EntityUid uid, Vector2 local, bool anchored, bool exists)>();
        var closets = new List<(EntityUid uid, Vector2 local, bool anchored, bool exists)>();
        var oxygens = new List<(EntityUid uid, Vector2 local, bool anchored, bool exists)>();
        var pickaxes = new List<(EntityUid uid, Vector2 local, bool anchored, bool exists)>();
        int totalOnGrid = 0;

        var q = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (q.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.MapID != mapId || xform.GridUid != outpostUid)
                continue;

            totalOnGrid++;
            var entry = (uid, xform.LocalPosition, xform.Anchored, SEntMan.EntityExists(uid));
            switch (meta.EntityPrototype?.ID)
            {
                case "LockerMinerFilled":           miners.Add(entry); break;
                case "ClosetEmergencyFilledRandom": closets.Add(entry); break;
                case "OxygenCanister":              oxygens.Add(entry); break;
                case "Pickaxe":                     pickaxes.Add(entry); break;
            }
        }

        lines.Add($"  [on outpost grid] totalEntities={totalOnGrid}");

        lines.Add($"  [on outpost grid] LockerMinerFilled count={miners.Count}");
        foreach (var (uid, local, anc, ex) in miners.OrderBy(t => t.local.X).ThenBy(t => t.local.Y))
            lines.Add($"    uid={uid} local=({local.X:F3},{local.Y:F3}) anchored={anc} exists={ex}");

        lines.Add($"  [on outpost grid] ClosetEmergencyFilledRandom count={closets.Count}");
        foreach (var (uid, local, anc, ex) in closets)
            lines.Add($"    uid={uid} local=({local.X:F3},{local.Y:F3}) anchored={anc} exists={ex} " +
                      $"target@(-19.5,-11.5) delta=({local.X + 19.5:F3},{local.Y + 11.5:F3})");

        lines.Add($"  [on outpost grid] OxygenCanister count={oxygens.Count}");
        foreach (var (uid, local, anc, ex) in oxygens)
            lines.Add($"    uid={uid} local=({local.X:F3},{local.Y:F3}) anchored={anc} exists={ex} " +
                      $"target@(-14.5,5.5) delta=({local.X + 14.5:F3},{local.Y - 5.5:F3})");

        lines.Add($"  [on outpost grid] Pickaxe count={pickaxes.Count}");
        foreach (var (uid, local, anc, ex) in pickaxes)
            lines.Add($"    uid={uid} local=({local.X:F3},{local.Y:F3}) anchored={anc} exists={ex}");

        return lines;
    }

    // ---------------------------------------------------------------------
    // Same prototypes found elsewhere on the same map. Reported separately and
    // never used as evidence that the outpost contains them.
    // ---------------------------------------------------------------------

    private List<string> DescribeIncidental(MapId mapId, EntityUid outpostUid)
    {
        var lines = new List<string>();
        var other = new Dictionary<EntityUid, List<string>>();

        var q = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (q.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.MapID != mapId || xform.GridUid is not { } otherGrid || otherGrid == outpostUid)
                continue;

            var id = meta.EntityPrototype?.ID;
            if (id is not ("LockerMinerFilled" or "ClosetEmergencyFilledRandom" or "OxygenCanister" or "Pickaxe"))
                continue;

            if (!other.TryGetValue(otherGrid, out var list))
            {
                list = new List<string>();
                other[otherGrid] = list;
            }
            list.Add($"{id}@({xform.LocalPosition.X:F2},{xform.LocalPosition.Y:F2})");
        }

        if (other.Count == 0)
        {
            lines.Add("  [other grids on same map] no matching entities");
            return lines;
        }

        lines.Add($"  [other grids on same map] {other.Count} grid(s) with matching prototypes:");
        foreach (var (grid, list) in other)
            lines.Add($"    grid={grid} count={list.Count}: {string.Join(", ", list)}");

        return lines;
    }

    private void DumpOutpostCensus(int seed, EntityUid outpostUid)
    {
        // Round 3c: chunk census of the outpost grid. Chunks are the unit that
        // map loading works in, so a missing chunk column explains missing entities.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== CHUNKS seed={seed} outpost={outpostUid} ===");

        if (!SEntMan.TryGetComponent<MapGridComponent>(outpostUid, out var grid))
        {
            sb.AppendLine("  no MapGridComponent");
        }
        else
        {
            var chunkSize = MapGridComponent.DefaultChunkSize;
            sb.AppendLine($"  ChunkCount={grid.ChunkCount} ChunkSize={chunkSize}");

            // Walk the chunk index range covering the grid AABB.
            var aabb = grid.LocalAABB;
            var minChunk = SharedMapSystem.GetChunkIndices(new Vector2(aabb.Left, aabb.Bottom), chunkSize);
            var maxChunk = SharedMapSystem.GetChunkIndices(new Vector2(aabb.Right, aabb.Top), chunkSize);

            for (var cx = minChunk.X - 1; cx <= maxChunk.X + 1; cx++)
            {
                for (var cy = minChunk.Y - 1; cy <= maxChunk.Y + 1; cy++)
                {
                    var index = new Vector2i(cx, cy);
                    var has = grid.HasChunk(index);

                    // Count non-empty tiles inside the chunk to see if it carries data.
                    int filled = 0;
                    if (has)
                    {
                        var mapSystem = SEntMan.System<SharedMapSystem>();
                        for (var lx = 0; lx < chunkSize; lx++)
                        {
                            for (var ly = 0; ly < chunkSize; ly++)
                            {
                                var tile = new Vector2i(cx * chunkSize + lx, cy * chunkSize + ly);
                                if (mapSystem.TryGetTileRef(outpostUid, grid, tile, out var tileRef) &&
                                    !tileRef.Tile.IsEmpty)
                                {
                                    filled++;
                                }
                            }
                        }
                    }

                    sb.AppendLine($"  chunk {index}: has={has} filledTiles={filled}");
                }
            }
        }

        try
        {
            System.IO.File.AppendAllText("/tmp/lavaland-chunks.log", sb.ToString());
        }
        catch { }
    }

    private void DumpOutpostChunkDetail(int seed, EntityUid outpostUid)
    {
        const int chunkSize = MapGridComponent.DefaultChunkSize;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== CHUNKDETAIL seed={seed} outpost={outpostUid} ===");

        var perChunk = new Dictionary<Vector2i, (int total, int anchored, int parentedToGrid, List<string> samples)>();

        var q = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (q.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.GridUid != outpostUid)
                continue;

            var chunk = new Vector2i(
                (int)Math.Floor(xform.LocalPosition.X / chunkSize),
                (int)Math.Floor(xform.LocalPosition.Y / chunkSize));

            if (!perChunk.TryGetValue(chunk, out var agg))
            {
                agg = (0, 0, 0, new List<string>());
                perChunk[chunk] = agg;
            }

            var id = meta.EntityPrototype?.ID ?? "<null>";
            var parentIsGrid = xform.ParentUid == outpostUid;
            agg.total++;
            if (xform.Anchored) agg.anchored++;
            if (parentIsGrid) agg.parentedToGrid++;

            if (agg.samples.Count < 6)
            {
                agg.samples.Add($"{id} uid={uid} parent={(xform.ParentUid is { } p ? p.ToString() : "null")}" +
                                $"(isGrid={parentIsGrid}) anchored={xform.Anchored}" +
                                $" local=({xform.LocalPosition.X:F3},{xform.LocalPosition.Y:F3})" +
                                $" world=({xform.WorldPosition.X:F3},{xform.WorldPosition.Y:F3})");
            }

            perChunk[chunk] = agg;
        }

        foreach (var (chunk, agg) in perChunk.OrderBy(kv => kv.Key.X).ThenBy(kv => kv.Key.Y))
        {
            sb.AppendLine($"  chunk {chunk}: total={agg.total} anchored={agg.anchored} parentedToGrid={agg.parentedToGrid}");
            foreach (var s in agg.samples)
                sb.AppendLine($"      {s}");
        }

        try
        {
            System.IO.File.AppendAllText("/tmp/lavaland-chunkdetail.log", sb.ToString());
        }
        catch { }
    }

    private void DumpOutpostLocate(int seed, EntityUid outpostUid)
    {
        // Where did the target entities end up? Search the whole server for them,
        // including entities that were re-parented away from the outpost grid or
        // pushed into nullspace.
        var wanted = new HashSet<string>(StringComparer.Ordinal)
        {
            "LockerMinerFilled", "ClosetEmergencyFilledRandom", "OxygenCanister",
            "Pickaxe", "RadioHandheld", "EmergencyOxygenTankFilled", "ToolboxEmergencyFilled",
            "MobSpawnCrabGold", "WoodenSupportBeam", "SpaceHeater",
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== LOCATE seed={seed} outpost={outpostUid} ===");

        var q = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (q.MoveNext(out var uid, out var meta, out var xform))
        {
            var id = meta.EntityPrototype?.ID;
            if (id is null || !wanted.Contains(id))
                continue;

            sb.AppendLine($"  {id} uid={uid} mapId={xform.MapID} " +
                          $"grid={(xform.GridUid is { } g ? g.ToString() : "null")} " +
                          $"parent={(xform.ParentUid is { } p ? p.ToString() : "null")} " +
                          $"local=({xform.LocalPosition.X:F2},{xform.LocalPosition.Y:F2}) " +
                          $"world=({xform.WorldPosition.X:F2},{xform.WorldPosition.Y:F2}) " +
                          $"anchored={xform.Anchored} " +
                          $"onOutpost={(xform.GridUid == outpostUid)}");
        }

        try
        {
            System.IO.File.AppendAllText("/tmp/lavaland-locate.log", sb.ToString());
        }
        catch { }
    }

    private void DumpOutpostCensusOld(int seed, EntityUid outpostUid)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var positions = new Dictionary<string, List<(float x, float y)>>(StringComparer.Ordinal);

        var q = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (q.MoveNext(out _, out var meta, out var xform))
        {
            if (xform.GridUid != outpostUid)
                continue;

            var id = meta.EntityPrototype?.ID ?? "<null>";
            counts.TryGetValue(id, out var n);
            counts[id] = n + 1;

            if (!positions.TryGetValue(id, out var list))
            {
                list = new List<(float x, float y)>();
                positions[id] = list;
            }
            list.Add((xform.LocalPosition.X, xform.LocalPosition.Y));
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== CENSUS seed={seed} outpost={outpostUid} total={counts.Values.Sum()} ===");
        foreach (var (id, n) in counts.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            var pos = positions[id];
            sb.AppendLine($"  {id}\tcount={n}\tfirst=({pos[0].x:F2},{pos[0].y:F2})");
        }

        try
        {
            System.IO.File.AppendAllText("/tmp/lavaland-census.log", sb.ToString());
        }
        catch { }
    }

    private static void Report(string block)
    {
        TestContext.Out.WriteLine(block);
        try { System.IO.File.AppendAllText("/tmp/lavaland-diag.log", block + "\n\n"); } catch { }
    }
}
