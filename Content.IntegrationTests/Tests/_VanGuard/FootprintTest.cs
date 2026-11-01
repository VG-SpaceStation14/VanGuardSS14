using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._VanGuard.Footprints;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Gravity;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;

namespace Content.IntegrationTests.Tests._VanGuard;

[TestFixture]
public sealed class FootprintTest : GameTest
{
    [Test]
    public async Task DryWalkingLeavesNoFootprints()
    {
        var (_, mob) = await PrepareWalker();
        await Move(mob);

        Assert.That(await CountFootprints(), Is.Zero, "A dry walker must not leave footprints");
    }

    [Test]
    public async Task CarrierLiquidIsConsumedAndFootprintsStop()
    {
        var (solutions, mob) = await PrepareWalker();
        var foots = GetFoots(solutions, mob);

        await Server.WaitPost(() => solutions.ForceAddSolution(foots, new Solution("Blood", 2)));

        await Move(mob);
        var firstCount = await CountFootprints();
        Assert.That(firstCount, Is.GreaterThan(0), "A wet walker should leave footprints");

        var printHasBlood = false;
        await Server.WaitAssertion(() =>
        {
            var prints = SEntMan.EntityQueryEnumerator<FootprintComponent>();
            while (prints.MoveNext(out var uid, out var print))
            {
                if (!solutions.TryGetSolution(uid, print.ContainerName, out _, out var printSolution))
                    continue;

                if (printSolution.Contents.Any(reagent => reagent.Reagent.Prototype == "Blood"))
                    printHasBlood = true;
            }
        });

        Assert.That(printHasBlood, Is.True, "Footprint should carry the reagent transferred from the walker");
        var remaining = await GetVolume(solutions, foots);
        Assert.That(remaining, Is.LessThan(2f), "Walking should consume the carried liquid");

        await Server.WaitPost(() =>
        {
            foots.Comp.Solution.RemoveAllSolution();
            var existing = new List<EntityUid>();
            var query = SEntMan.EntityQueryEnumerator<FootprintComponent>();
            while (query.MoveNext(out var uid, out _))
                existing.Add(uid);

            foreach (var uid in existing)
                SEntMan.DeleteEntity(uid);
        });

        var emptyCount = await CountFootprints();
        await Move(mob);
        Assert.That(await CountFootprints(), Is.EqualTo(emptyCount), "No new footprints should appear after the carrier is empty");

        await Server.WaitPost(() =>
        {
            var xform = SEntMan.GetComponent<TransformComponent>(mob);
            xform.LocalPosition = Vector2.Zero;
            SEntMan.GetComponent<FootprintEmitterComponent>(mob).LastStepPosition = Vector2.Zero;
            solutions.ForceAddSolution(foots, new Solution("Blood", 1));
        });

        Assert.That(await GetVolume(solutions, foots), Is.GreaterThan(0f), "Carrier should be refilled");
        await Move(mob);
        Assert.That(await CountFootprints(), Is.GreaterThan(emptyCount), "Stepping in liquid again should enable footprints");
    }

    [Test]
    public async Task PuddleContactTransfersLiquidToWalker()
    {
        var (solutions, mob) = await PrepareWalker();
        var foots = GetFoots(solutions, mob);

        EntityUid puddle = default;
        await Server.WaitPost(() =>
        {
            puddle = SEntMan.SpawnAtPosition("PuddleBloodSmall", SEntMan.GetComponent<TransformComponent>(mob).Coordinates);
            var ev = new EndCollideEvent(
                puddle,
                mob,
                string.Empty,
                string.Empty,
                null!,
                null!,
                null!,
                null!);
            SEntMan.EventBus.RaiseLocalEvent(puddle, ref ev);
        });

        Assert.That(await GetVolume(solutions, foots), Is.GreaterThan(0f),
            "Leaving a reagent puddle should transfer liquid to the walker");
    }

    private async Task<(SharedSolutionContainerSystem Solutions, EntityUid Mob)> PrepareWalker()
    {
        var map = await Pair.CreateTestMap();
        var coords = new EntityCoordinates(map.Grid, Vector2.Zero);
        EntityUid mob = default;

        await Server.WaitPost(() =>
        {
            var gravity = SEntMan.TryGetComponent<GravityComponent>(map.Grid, out var existing)
                ? existing
                : SEntMan.AddComponent<GravityComponent>(map.Grid);
            gravity!.Enabled = true;

            mob = SEntMan.SpawnAtPosition("MobHuman", coords);
        });

        await Server.WaitAssertion(() =>
        {
            var input = SEntMan.GetComponent<InputMoverComponent>(mob);
            input.HeldMoveButtons = MoveButtons.Right;
        });

        return (SEntMan.System<SharedSolutionContainerSystem>(), mob);
    }

    private async Task Move(EntityUid mob, MoveButtons direction = MoveButtons.Right)
    {
        await Server.WaitPost(() =>
        {
            var input = SEntMan.GetComponent<InputMoverComponent>(mob);
            input.HeldMoveButtons = direction;
        });

        await RunSeconds(1.5f);
    }

    private Entity<SolutionComponent> GetFoots(SharedSolutionContainerSystem solutions, EntityUid mob)
    {
        var emitter = SEntMan.GetComponent<FootprintEmitterComponent>(mob);
        Assert.That(solutions.ResolveSolution(mob, emitter.FootsSolutionName, ref emitter.FootsSolution, out var foots), Is.True);
        return emitter.FootsSolution!.Value;
    }

    private async Task<float> GetVolume(SharedSolutionContainerSystem solutions, Entity<SolutionComponent> foots)
    {
        var volume = 0f;
        await Server.WaitAssertion(() => volume = foots.Comp.Solution.Volume.Float());
        return volume;
    }

    private async Task<int> CountFootprints()
    {
        var count = 0;
        await Server.WaitAssertion(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<FootprintComponent>();
            while (query.MoveNext(out _, out _))
                count++;
        });
        return count;
    }
}
