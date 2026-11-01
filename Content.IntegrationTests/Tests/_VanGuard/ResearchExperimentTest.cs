using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._VanGuard.Research.Components;
using Content.Server._VanGuard.Research.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._VanGuard;

[TestFixture]
public sealed class ResearchExperimentTest : GameTest
{
    [Test]
    public async Task SolutionConditionAcceptsMatchingPuddleAndRejectsOtherPuddles()
    {
        var map = await Pair.CreateTestMap();
        var coords = new EntityCoordinates(map.Grid, Vector2.Zero);

        EntityUid vomit = default;
        EntityUid water = default;
        await Server.WaitPost(() =>
        {
            vomit = SEntMan.SpawnAtPosition("PuddleVomit", coords);
            water = SEntMan.SpawnAtPosition("PuddleEgg", coords.Offset(new Vector2(1f, 0f)));
        });

        await Server.WaitAssertion(() =>
        {
            var scanner = SEntMan.System<ExperimentScannerSystem>();
            var matching = new StationExperimentOrderData
            {
                Prototype = "ExperimentVomitPuddle",
            };
            var wrong = new StationExperimentOrderData
            {
                Prototype = "ExperimentVomitPuddle",
            };

            Assert.That(scanner.TryProcessScan(default, matching, vomit), Is.True);
            Assert.That(matching.ProgressCurrent, Is.EqualTo(1));
            Assert.That(scanner.TryProcessScan(default, wrong, water), Is.False);
            Assert.That(wrong.ProgressCurrent, Is.EqualTo(0));
        });
    }
}
