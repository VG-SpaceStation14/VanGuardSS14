using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._VanGuard.Lavaland.Components;
using Content.Server._VanGuard.Lavaland.Systems;
using Content.Server.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.VanGuard.Tests.Tests._VanGuard.Lavaland;

/// <summary>
/// TEMPORARY diagnostic test. Tracks the life cycle of every grid on the
/// Lavaland planet to find out where an unexpected grid comes from and how it
/// ends up next to the outpost.
///
/// Must be removed once the investigation is finished.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class LavalandGridOriginTest : GameTest
{
    private static readonly string[] Seeds = { "13", "100", "424242" };

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    [Test]
    public async Task TraceGridOrigin()
    {
        var report = new System.Text.StringBuilder();

        foreach (var seed in Seeds)
            await RunSeed(seed, report);

        await File.WriteAllTextAsync("/tmp/lavaland-gridorigin.log", report.ToString());
        TestContext.Out.WriteLine(report.ToString());
    }

    private async Task RunSeed(string seed, System.Text.StringBuilder report)
    {
        report.AppendLine($"########## SEED {seed} ##########");

        var ticker = Server.System<ServerGameTicker>();
        EntityUid rule = EntityUid.Invalid;

        try
        {
            await Server.WaitPost(() =>
            {
                LavalandGridOriginSystem.Clear();
                LavalandGridOriginSystem.Enabled = true;
            });

            await Server.WaitPost(() =>
            {
                rule = ticker.AddGameRule(new EntProtoId("LavalandLoader"))!.Value;
                SEntMan.GetComponent<LavalandOutpostRuleComponent>(rule).Seed = int.Parse(seed);
            });

            // Sample a snapshot every tick while the rule is loading. The rule
            // executes its startup in successive system ticks, so a snapshot per
            // tick can pinpoint the moment when an unexpected grid appears or
            // disappears.
            for (var tick = 0; tick < 30; tick++)
            {
                await Pair.RunTicksSync(1);
                var currentTick = tick;
                await Server.WaitPost(() =>
                {
                    LavalandGridOriginSystem.SampleTick(currentTick);
                });
            }

            // Identify the planet map and the outpost grid.
            await Server.WaitPost(() =>
            {
                var comp = SEntMan.GetComponent<LavalandOutpostRuleComponent>(rule);
                LavalandGridOriginSystem.PlanetMap = comp.Map ?? EntityUid.Invalid;
                LavalandGridOriginSystem.OutpostGrid = comp.OutpostGrid ?? EntityUid.Invalid;
            });

            await Server.WaitPost(() => LavalandGridOriginSystem.Snapshot("after-rule-load"));

            // Sample every grid that overlaps the outpost footprint, so the one that
            // ends up on top of the outpost can be followed tick by tick.
            await Server.WaitPost(() =>
            {
                var entMan = SEntMan;
                var outpost = LavalandGridOriginSystem.OutpostGrid;
                if (!entMan.TryGetComponent<MapGridComponent>(outpost, out var outpostGrid))
                    return;

                var outpostXform = entMan.GetComponent<TransformComponent>(outpost);
                var outpostBounds = outpostGrid.LocalAABB
                    .Translated(outpostXform.WorldPosition)
                    .Enlarged(18f);

                var query = entMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
                while (query.MoveNext(out var gridUid, out var gridComp, out var xform))
                {
                    if (gridUid == outpost)
                        continue;

                    var bounds = gridComp.LocalAABB.Translated(xform.WorldPosition);
                    if (!outpostBounds.Intersects(bounds))
                        continue;

                    LavalandGridOriginSystem.SampledGrids.Add(gridUid);
                }
            });

            // Let the physics settle so that any grid traversal becomes visible,
            // sampling the interesting grids on every tick.
            for (var tick = 0; tick < 15; tick++)
            {
                await Pair.RunTicksSync(1);
                var currentTick = tick;
                await Server.WaitPost(() => LavalandGridOriginSystem.SampleTick(currentTick));
            }

            await Server.WaitPost(() => LavalandGridOriginSystem.Snapshot("after-10-ticks"));

            List<string> lines = new();
            await Server.WaitPost(() => lines = LavalandGridOriginSystem.Log.ToList());

            report.AppendLine($"  outpostGrid={LavalandGridOriginSystem.OutpostGrid.Id} " +
                              $"planetMap={LavalandGridOriginSystem.PlanetMap.Id}");
            report.AppendLine($"  --- GRID LIFE CYCLE ({lines.Count} lines) ---");
            foreach (var line in lines)
                report.AppendLine("      " + line);
        }
        finally
        {
            await Server.WaitPost(() => LavalandGridOriginSystem.Enabled = false);

            if (rule.Valid)
                await Server.WaitPost(() => ticker.EndGameRule(rule));
        }
    }
}
