using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Gravity;
using Content.Shared._VanGuard.Footprints;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Standing;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;

namespace Content.Server._VanGuard.Footprints;

/// <summary>
/// Emits foot and drag marks when a footprint emitter moves far enough across a grid.
/// </summary>
public sealed partial class FootprintSystem : EntitySystem
{
    private const int MaxFootprintsOnTile = 6;
    private const int MaxDragMarksOnTile = 3;

    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedTransformSystem _transforms = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private GravitySystem _gravity = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPuddleSystem _puddles = default!;

    private EntityQuery<SolutionManagerComponent> _solutionManagers;
    private EntityQuery<PhysicsComponent> _bodies;
    private EntityQuery<StandingStateComponent> _standing;
    private EntityQuery<FootprintComponent> _prints;
    private EntityQuery<PressureProtectionComponent> _pressure;
    private readonly HashSet<Entity<FootprintComponent>> _nearbyPrints = new();

    public override void Initialize()
    {
        base.Initialize();

        _solutionManagers = GetEntityQuery<SolutionManagerComponent>();
        _bodies = GetEntityQuery<PhysicsComponent>();
        _standing = GetEntityQuery<StandingStateComponent>();
        _prints = GetEntityQuery<FootprintComponent>();
        _pressure = GetEntityQuery<PressureProtectionComponent>();

        SubscribeLocalEvent<FootprintEmitterComponent, ComponentStartup>(OnEmitterStarted);
        SubscribeLocalEvent<FootprintEmitterComponent, ComponentInit>(OnEmitterInit);
        SubscribeLocalEvent<FootprintEmitterComponent, MoveEvent>(OnEmitterMoved);
        SubscribeLocalEvent<FootprintComponent, SolutionChangedEvent>(OnFootprintSolutionChanged);
    }

    private void OnFootprintSolutionChanged(Entity<FootprintComponent> ent, ref SolutionChangedEvent args)
    {
        if (args.Solution.Comp.Id != ent.Comp.ContainerName
            || !TryComp<AppearanceComponent>(ent, out var appearance)
            || !_solutions.ResolveSolution(ent.Owner,
                ent.Comp.ContainerName,
                ref ent.Comp.SolutionContainer,
                out var solution))
            return;

        _appearance.SetData(ent.Owner,
            FootprintVisualParameter.TrackColor,
            _puddles.GetSolutionColor(solution),
            appearance);
    }

    private void OnEmitterInit(Entity<FootprintEmitterComponent> ent, ref ComponentInit args)
    {
        // EnsureSolution returns false when it creates a new default solution, but still fills
        // the out parameter. Both solutions need their capacity even in that case.
        _solutions.EnsureSolution(ent.Owner, ent.Comp.FootsSolutionName, out var foots);
        foots.Comp.Solution.MaxVolume = FixedPoint2.New(5);

        _solutions.EnsureSolution(ent.Owner, ent.Comp.BodySurfaceSolutionName, out var body);
        body.Comp.Solution.MaxVolume = FixedPoint2.New(15);
    }

    private void OnEmitterStarted(Entity<FootprintEmitterComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.WalkStepInterval = MathF.Max(0f, ent.Comp.WalkStepInterval + _random.NextFloat(-0.05f, 0.05f));
    }

    private void OnEmitterMoved(Entity<FootprintEmitterComponent> ent, ref MoveEvent args)
    {
        if (TerminatingOrDeleted(ent.Owner) || !_solutionManagers.TryComp(ent, out var manager))
            return;

        var standing = !_standing.TryComp(ent, out var pose) || pose.Standing;
        if (!TryResolveEmitterSolution(ent, manager, standing, out var solution, out var solutionEnt))
            return;

        if (solution.Volume <= 0
            || !_bodies.TryComp(ent, out var body)
            || body.BodyStatus == BodyStatus.InAir
            || _gravity.IsWeightless(ent.Owner))
            return;

        var xform = Transform(ent);
        var map = _transforms.GetMapCoordinates((ent, xform));
        if (!_maps.TryFindGridAt(map, out var gridUid, out var grid))
            return;

        var interval = standing ? ent.Comp.WalkStepInterval : ent.Comp.DragMarkInterval;
        if ((xform.LocalPosition - ent.Comp.LastStepPosition).Length() <= interval)
            return;

        var tile = _maps.GetTileRef((gridUid, grid), xform.Coordinates);
        if (!HasRoomForAnotherPrint(gridUid, tile.GridIndices, standing))
            return;

        ent.Comp.IsRightStep = !ent.Comp.IsRightStep;
        var print = CreatePrint(gridUid, ent, solution, xform, standing);
        ConfigurePrint(print, ent, solution, solutionEnt, xform, standing);
        ent.Comp.LastStepPosition = xform.LocalPosition;
    }

    private bool TryResolveEmitterSolution(
        Entity<FootprintEmitterComponent> ent,
        SolutionManagerComponent manager,
        bool standing,
        out Solution solution,
        out Entity<SolutionComponent> solutionEnt)
    {
        solution = default!;
        solutionEnt = default;
        var managerEnt = (ent.Owner, manager);
        if (standing)
        {
            var resolved = _solutions.ResolveSolution(
                managerEnt,
                ent.Comp.FootsSolutionName,
                ref ent.Comp.FootsSolution,
                out var foots);

            if (!resolved || foots == null)
                return false;

            if (foots.MaxVolume.Float() <= 0)
                foots.MaxVolume = FixedPoint2.New(5);

            solution = foots;
            solutionEnt = ent.Comp.FootsSolution!.Value;
            return true;
        }

        var resolvedDrag = _solutions.ResolveSolution(
            managerEnt,
            ent.Comp.BodySurfaceSolutionName,
            ref ent.Comp.BodySurfaceSolution,
            out var body);

        if (!resolvedDrag || body == null)
            return false;

        if (body.MaxVolume.Float() <= 0)
            body.MaxVolume = FixedPoint2.New(15);

        solution = body;
        solutionEnt = ent.Comp.BodySurfaceSolution!.Value;
        return true;
    }

    private bool HasRoomForAnotherPrint(EntityUid grid, Vector2i tile, bool standing)
    {
        _nearbyPrints.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, tile, _nearbyPrints);

        var feet = 0;
        var drags = 0;
        foreach (var print in _nearbyPrints)
        {
            if (print.Comp.PrintType == PrintType.Foot)
                feet++;
            else
                drags++;
        }

        return standing ? feet < MaxFootprintsOnTile : drags < MaxDragMarksOnTile;
    }

    private EntityUid CreatePrint(
        EntityUid grid,
        Entity<FootprintEmitterComponent> emitter,
        Solution solution,
        TransformComponent xform,
        bool standing)
    {
        var coordinates = PrintCoordinates(grid, emitter.Comp, xform, standing);
        var print = Spawn(standing ? emitter.Comp.FootprintPrototype : emitter.Comp.DragMarkPrototype, coordinates);

        if (!TryComp<AppearanceComponent>(print, out var appearance))
            return print;

        var type = standing ? ClassifyPrint(emitter.Owner) : FootprintVisualType.DragMark;
        var state = RandomState(type, emitter.Comp);
        _appearance.SetData(print, FootprintVisualParameter.VisualState, state, appearance);

        _appearance.SetData(print, FootprintVisualParameter.TrackColor, _puddles.GetSolutionColor(solution), appearance);

        return print;
    }

    private EntityCoordinates PrintCoordinates(
        EntityUid grid,
        FootprintEmitterComponent emitter,
        TransformComponent xform,
        bool standing)
    {
        if (!standing)
            return new EntityCoordinates(grid, xform.LocalPosition);

        var turn = emitter.IsRightStep
            ? Angle.FromDegrees(180f) + xform.LocalRotation
            : xform.LocalRotation;
        var offset = new Angle(turn).RotateVec(emitter.PlacementOffset);
        return new EntityCoordinates(grid, xform.LocalPosition + offset);
    }

    private FootprintVisualType ClassifyPrint(EntityUid wearer)
    {
        if (_inventory.TryGetSlotEntity(wearer, "outerClothing", out var outer)
            && _pressure.TryComp(outer, out _))
            return FootprintVisualType.SuitFootprint;

        return _inventory.TryGetSlotEntity(wearer, "shoes", out _)
            ? FootprintVisualType.ShoeFootprint
            : FootprintVisualType.BareFootprint;
    }

    private string RandomState(FootprintVisualType type, FootprintEmitterComponent emitter)
    {
        var states = type switch
        {
            FootprintVisualType.BareFootprint => emitter.IsRightStep ? emitter.RightBareFootState : emitter.LeftBareFootState,
            FootprintVisualType.ShoeFootprint => emitter.ShoeFootState,
            FootprintVisualType.SuitFootprint => emitter.PressureSuitFootState,
            FootprintVisualType.DragMark => emitter.DraggingStates,
            _ => emitter.LeftBareFootState,
        };

        return _random.Pick(states);
    }

    private void ConfigurePrint(
        EntityUid print,
        Entity<FootprintEmitterComponent> emitter,
        Solution solution,
        Entity<SolutionComponent> emitterSolution,
        TransformComponent xform,
        bool standing)
    {
        if (Transform(print) is { } printXform)
        {
            printXform.LocalRotation = standing
                ? xform.LocalRotation + Angle.FromDegrees(180f)
                : (xform.LocalPosition - emitter.Comp.LastStepPosition).ToAngle() + Angle.FromDegrees(-90f);
        }

        if (solution.Volume <= 0
            || !_prints.TryComp(print, out var printComp)
            || !_solutions.ResolveSolution(print, printComp.ContainerName, ref printComp.SolutionContainer, out _))
            return;

        var volume = standing ? emitter.Comp.TransferVolumeFoot : emitter.Comp.TransferVolumeDragMark;
        var split = _solutions.SplitSolution(emitterSolution, volume);
        _solutions.AddSolution(printComp.SolutionContainer!.Value, split);
    }
}
