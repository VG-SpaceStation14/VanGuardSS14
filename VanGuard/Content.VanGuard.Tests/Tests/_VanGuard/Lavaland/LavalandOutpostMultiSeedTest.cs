#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._VanGuard.Lavaland.Components;
using Content.Server.GameTicking;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.VanGuard.Tests.Tests._VanGuard.Lavaland;

/// <summary>
///     Regression coverage for the Lavaland outpost generation order. The
///     outpost is the very last generation step; if anything regresses and
///     its content is loaded while a transient dungeon grid is still alive,
///     entities end up parented to those extra grids and vanish from the
///     outpost. A single seed only tests one POI layout, so several seeds
///     are used to catch placement-dependent regressions.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class LavalandOutpostMultiSeedTest : GameTest
{
    /// <summary>
    /// POIs move around with the seed. Each of these seeds produces a
    /// different POI layout, so a run over them catches placement-dependent
    /// regressions that a single seed would not.
    /// </summary>
    private static readonly int[] Seeds =
    {
        1, 42, 314, 2718, 424242, 1337, 2025, 65537,
    };

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    [Test]
    public async Task OutpostContentSurvivesEverySeed()
    {
        var ticker = Server.System<ServerGameTicker>();

        foreach (var seed in Seeds)
        {
            Entity<LavalandOutpostRuleComponent> rule = default;

            await Server.WaitPost(() =>
            {
                var ruleUid = ticker.AddGameRule(new EntProtoId("LavalandLoader"));
                Assert.That(ruleUid, Is.Not.Null, $"Seed {seed}: LavalandLoader rule was not added.");

                rule = (ruleUid!.Value.Owner, SEntMan.GetComponent<LavalandOutpostRuleComponent>(ruleUid.Value.Owner));
                rule.Comp.Seed = seed;
            });
            await Pair.RunUntilSynced();

            await AssertOutpostIsConsistent(seed, rule);
            TestContext.Progress.WriteLine($"OutpostContentSurvivesEverySeed: seed {seed} OK");

            await Server.WaitPost(() =>
            {
                if (SEntMan.EntityExists(rule.Owner))
                    SEntMan.DeleteEntity(rule.Owner);
            });
            await Pair.RunUntilSynced();
        }
    }

    private async Task AssertOutpostIsConsistent(int seed, Entity<LavalandOutpostRuleComponent> rule)
    {
        var label = $"Seed {seed}";

        Assert.That(rule.Comp.Map, Is.Not.Null, $"{label}: the rule did not create a map.");
        Assert.That(rule.Comp.MapId, Is.Not.Null, $"{label}: the rule did not record a map id.");
        Assert.That(rule.Comp.OutpostGrid, Is.Not.Null, $"{label}: the outpost grid was not loaded.");

        var mapUid = rule.Comp.Map!.Value;
        var mapId = rule.Comp.MapId!.Value;
        var outpostUid = rule.Comp.OutpostGrid!.Value;

        Assert.That(SEntMan.EntityExists(outpostUid), Is.True,
            $"{label}: the outpost grid entity does not exist.");
        Assert.That(SEntMan.HasComponent<MapGridComponent>(outpostUid), Is.True,
            $"{label}: the outpost grid has no MapGridComponent.");
        Assert.That(SEntMan.GetComponent<TransformComponent>(outpostUid).MapID, Is.EqualTo(mapId),
            $"{label}: the outpost grid is not on the Lavaland map.");

        var outpostBounds = SEntMan.GetComponent<MapGridComponent>(outpostUid).LocalAABB
            .Translated(SEntMan.GetComponent<TransformComponent>(outpostUid).LocalPosition);
        var extraGrids = new List<string>();

        // The filled miner locker only exists in mining_outpost.yml. If it is
        // missing, the outpost did not load its content. Pickaxes also exist
        // in dungeon maps, so they cannot be used as an exclusive marker.
        var lockerCount = 0;
        var totalOutpostContent = 0;
        var foreignContent = 0;
        var query = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            if (uid == mapUid || uid == outpostUid)
                continue;

            var proto = meta.EntityPrototype?.ID;
            var onOutpostGrid = xform.GridUid == outpostUid;
            var underOutpost = IsUnderOutpost(xform.ParentUid, outpostUid, mapUid);

            if (onOutpostGrid || underOutpost)
            {
                totalOutpostContent++;

                if (proto == "LockerMinerFilled")
                    lockerCount++;

                continue;
            }

            // The filled miner locker is exclusive to the outpost. Finding it
            // anywhere else means content leaked onto a foreign grid.
            if (proto == "LockerMinerFilled")
            {
                foreignContent++;
                continue;
            }

            // Anything else with outpost-exclusive naming is also a leak; the
            // filled miner belt only exists in mining_outpost.yml.
            if (proto is "MiningBelt")
                foreignContent++;
        }

        Assert.That(totalOutpostContent, Is.GreaterThan(0),
            $"{label}: the outpost grid is empty — no content was loaded.");
        Assert.That(lockerCount, Is.GreaterThan(0),
            $"{label}: the filled miner locker is missing from the outpost.");
        Assert.That(foreignContent, Is.Zero,
            $"{label}: outpost-exclusive content ended up on a foreign grid.");

        // No POI may overlap the outpost's footprint. The generation order
        // reserves the area before any POI is placed, so a marker inside it
        // means the reservation failed.
        var poiInFootprint = 0;
        var poiQuery = SEntMan.EntityQueryEnumerator<Content.Server._VanGuard.Lavaland.Components.LavalandPoiMarkerComponent, TransformComponent>();
        while (poiQuery.MoveNext(out _, out var poiMarker, out var poiXform))
        {
            if (poiXform.MapID != mapId)
                continue;

            if (outpostBounds.Contains(poiXform.WorldPosition))
            {
                poiInFootprint++;
                extraGrids.Add($"poi-marker/{poiMarker.Name} pos={poiXform.WorldPosition}");
            }
        }

        Assert.That(poiInFootprint, Is.Zero,
            $"{label}: {poiInFootprint} POI markers landed inside the outpost footprint.");

        // No POI may leave a grid of its own behind, and no foreign grid may
        // sit inside the outpost's footprint. The outpost is the only
        // legitimate grid in its own area; the planet and its grid-split
        // fragments are expected elsewhere on the map.
        var separateGridCount = 0;
        var gridQuery = SEntMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var gridUid, out _, out var gridXform))
        {
            if (gridXform.MapID != mapId || gridUid == mapUid || gridUid == outpostUid)
                continue;

            if (IsUnderOutpost(gridXform.ParentUid, outpostUid, mapUid))
                continue;

            // Grid-split fragments of the planet are normal; only grids
            // overlapping the outpost's own area are a problem.
            var foreign = SEntMan.GetComponent<MapGridComponent>(gridUid).LocalAABB.Translated(gridXform.LocalPosition);
            if (!outpostBounds.Intersects(foreign))
                continue;

            separateGridCount++;
            var meta = SEntMan.GetComponent<MetaDataComponent>(gridUid);
            extraGrids.Add($"{gridUid}/{meta.EntityPrototype?.ID ?? "(null)"} parent={gridXform.ParentUid} pos={gridXform.LocalPosition}");
        }

        Assert.That(separateGridCount, Is.Zero,
            $"{label}: {separateGridCount} foreign grids overlap the outpost footprint — " +
            $"outpost={outpostUid} map={mapUid} leftover: [{string.Join("; ", extraGrids)}]");
    }

    private bool IsUnderOutpost(EntityUid candidate, EntityUid outpostUid, EntityUid mapUid)
    {
        var uid = candidate;
        for (var depth = 0; depth < 16 && uid.IsValid() && uid != mapUid; depth++)
        {
            if (uid == outpostUid)
                return true;

            if (!SEntMan.TryGetComponent<TransformComponent>(uid, out var xform))
                return false;

            uid = xform.ParentUid;
        }

        return false;
    }
}