using System.Linq;
using Content.Shared._VanGuard.Footprints;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids.Components;
using Content.Shared.Standing;
using Robust.Shared.Physics.Events;

namespace Content.Server._VanGuard.Footprints;

/// <summary>
/// Picks up a small amount of puddle reagent when a footstep emitter leaves the puddle.
/// </summary>
public sealed partial class PuddleFootprintSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PuddleFootprintComponent, EndCollideEvent>(OnPuddleLeft);
    }

    private void OnPuddleLeft(Entity<PuddleFootprintComponent> puddleEnt, ref EndCollideEvent args)
    {
        var walker = args.OtherEntity;
        if (TerminatingOrDeleted(puddleEnt) || TerminatingOrDeleted(walker))
            return;

        if (!TryComp<PuddleComponent>(puddleEnt, out var puddle)
            || !TryComp<FootprintEmitterComponent>(walker, out var emitter)
            || !TryComp<SolutionManagerComponent>(walker, out var manager)
            || !_solutions.ResolveSolution(puddleEnt.Owner, puddle.SolutionName, ref puddle.Solution, out var puddleSolution))
            return;

        if (puddleSolution.Contents.Count == 0)
            return;

        var total = puddleSolution.Contents.Sum(reagent => reagent.Quantity.Float());
        var water = puddleSolution.Contents
            .Where(reagent => reagent.Reagent.Prototype == "Water")
            .Sum(reagent => reagent.Quantity.Float());

        if (total <= 0f || water / total * 100f > puddleEnt.Comp.WaterThresholdPercent)
            return;

        var standing = !_standing.IsDown(walker);
        if (!TryResolveCarrierSolution(walker, emitter, manager, standing, out var carrier, out var carrierEnt))
            return;

        var free = carrier.MaxVolume.Float() - carrier.Volume.Float();
        if (free <= 0f)
            return;

        var transfer = MathF.Min(puddleEnt.Comp.TransferVolume, free);
        transfer = MathF.Min(transfer, puddleSolution.Volume.Float());
        if (transfer <= 0f)
            return;

        var split = _solutions.SplitSolution(puddle.Solution!.Value, transfer);
        _solutions.AddSolution(carrierEnt, split);
    }

    private bool TryResolveCarrierSolution(
        EntityUid walker,
        FootprintEmitterComponent emitter,
        SolutionManagerComponent manager,
        bool standing,
        out Solution solution,
        out Entity<SolutionComponent> solutionEnt)
    {
        var source = (walker, manager);

        if (standing)
        {
            var resolved = _solutions.ResolveSolution(
                source,
                emitter.FootsSolutionName,
                ref emitter.FootsSolution,
                out var foots);

            if (!resolved || foots == null)
            {
                solution = default!;
                solutionEnt = default;
                return false;
            }

            solution = foots;
            solutionEnt = emitter.FootsSolution!.Value;
            return true;
        }

        var resolvedBody = _solutions.ResolveSolution(
            source,
            emitter.BodySurfaceSolutionName,
            ref emitter.BodySurfaceSolution,
            out var body);

        if (!resolvedBody || body == null)
        {
            solution = default!;
            solutionEnt = default;
            return false;
        }

        solution = body;
        solutionEnt = emitter.BodySurfaceSolution!.Value;
        return true;
    }
}
