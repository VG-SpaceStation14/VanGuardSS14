using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._VanGuard.Footprints;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._VanGuard;

[TestFixture]
public sealed class MopAreaCleanupTest : GameTest
{
    [Test]
    public async Task MopCleansFootprintsAndPuddlesWithinRadiusOnly()
    {
        var map = await Pair.CreateTestMap();
        var center = new EntityCoordinates(map.Grid, Vector2.Zero);

        EntityUid mop = default;
        EntityUid user = default;
        EntityUid nearPrint = default;
        EntityUid farPrint = default;
        EntityUid puddle = default;
        EntityUid unrelated = default;

        await Server.WaitPost(() =>
        {
            mop = SEntMan.SpawnAtPosition("MopItem", center);
            user = SEntMan.SpawnAtPosition("MobHuman", center);
            nearPrint = SpawnPrint(center.Offset(new Vector2(0.2f, 0f)));
            farPrint = SpawnPrint(center.Offset(new Vector2(1f, 0f)));
            puddle = SEntMan.SpawnAtPosition("PuddleBloodSmall", center);
            unrelated = SEntMan.SpawnAtPosition("DrinkBeerCan", center.Offset(new Vector2(0.1f, 0.1f)));

            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var absorbent = SEntMan.GetComponent<AbsorbentComponent>(mop);
            Assert.That(solutions.TryGetSolution(mop, absorbent.SolutionName, out var mopSolution, out _), Is.True);
            solutions.ForceAddSolution(mopSolution!.Value, new Solution("Water", 50));

            foreach (var uid in new[] { nearPrint, farPrint })
            {
                var component = SEntMan.GetComponent<FootprintComponent>(uid);
                Assert.That(solutions.TryGetSolution(uid, component.ContainerName, out var solutionEnt, out _), Is.True);
                solutions.ForceAddSolution(solutionEnt!.Value, new Solution("Blood", 1));
            }
        });

        await Server.WaitAssertion(() =>
        {
            var targetXform = SEntMan.GetComponent<TransformComponent>(nearPrint);
            var ev = new AfterInteractEvent(user, mop, nearPrint, targetXform.Coordinates, true);
            SEntMan.EventBus.RaiseLocalEvent(mop, ev);
        });

        await Server.WaitAssertion(() =>
        {
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();

            Assert.That(HasReagent(solutions, nearPrint, "Blood"), Is.False,
                "Near footprint should be cleaned");
            Assert.That(HasReagent(solutions, nearPrint, "Water"), Is.True,
                "Near footprint should receive replacement water");
            Assert.That(HasReagent(solutions, farPrint, "Blood"), Is.True,
                "Footprint outside the radius must remain dirty");
            Assert.That(HasReagent(solutions, puddle, "Blood"), Is.False,
                "Puddle inside the radius should be cleaned");
            Assert.That(SEntMan.EntityExists(unrelated), Is.True,
                "Unrelated entities must not be removed");

            var absorbent = SEntMan.GetComponent<AbsorbentComponent>(mop);
            Assert.That(solutions.TryGetSolution(mop, absorbent.SolutionName, out _, out var mopSolution), Is.True);
            Assert.That(mopSolution.Contents.Any(entry => entry.Reagent.Prototype == "Blood"), Is.True,
                "Contaminants should be transferred to the mop solution");
        });
    }

    private EntityUid SpawnPrint(EntityCoordinates coords)
    {
        return SEntMan.SpawnAtPosition("Footstep", coords);
    }

    private bool HasReagent(SharedSolutionContainerSystem solutions, EntityUid uid, string reagent)
    {
        if (!SEntMan.TryGetComponent<PuddleComponent>(uid, out var puddle))
            return false;

        return solutions.TryGetSolution(uid, puddle.SolutionName, out _, out var solution)
               && solution.Contents.Any(entry => entry.Reagent.Prototype == reagent);
    }
}
