using System.Linq;
using Content.Server.Research.Systems;
using Content.Server._VanGuard.Research.Components;
using Content.Shared._VanGuard.Research.Components;
using Content.Shared.Fluids.Components;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Research.Components;
using Content.Shared.Tag;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Research.Systems;

/// <summary>
/// Handles the floor experiment scanner: gathers loose items and in-place targets from its tile,
/// scans them against the active order, and returns the collected items when finished.
/// </summary>
public sealed partial class ExperimentFloorScannerSystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> PipeTag = "Pipe";

    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ExperimentScannerSystem _scanner = default!;
    [Dependency] private TagSystem _tags = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExperimentFloorScannerComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<ExperimentFloorScannerComponent, ExperimentSelectOrderMessage>(OnSelected);
        SubscribeLocalEvent<ExperimentFloorScannerComponent, ExperimentAbandonOrderMessage>(OnAbandoned);
        SubscribeLocalEvent<ExperimentFloorScannerComponent, ExperimentSkipOrderMessage>(OnSkipped);
        SubscribeLocalEvent<ExperimentFloorScannerComponent, ExperimentFloorScannerPerformMessage>(OnPerform);
        SubscribeLocalEvent<ExperimentFloorScannerComponent, ResearchRegistrationChangedEvent>(OnRegistrationChanged);
        SubscribeLocalEvent<ExperimentFloorScannerComponent, ComponentStartup>(OnStarted);
    }

    private void OnStarted(Entity<ExperimentFloorScannerComponent> ent, ref ComponentStartup args)
    {
        _containers.EnsureContainer<Container>(ent, ExperimentFloorScannerComponent.ContainerId);
        SetVisual(ent, ExperimentFloorScannerVisualState.Idle);
        PushState(ent);
    }

    private void OnOpened(Entity<ExperimentFloorScannerComponent> ent, ref BoundUIOpenedEvent args)
    {
        PushState(ent);
    }

    private void OnRegistrationChanged(Entity<ExperimentFloorScannerComponent> ent, ref ResearchRegistrationChangedEvent args)
    {
        PushState(ent);
    }

    private void OnSelected(Entity<ExperimentFloorScannerComponent> ent, ref ExperimentSelectOrderMessage args)
    {
        if (!_scanner.IsFloorCompatibleOrder(args.Id, ent.Owner))
        {
            Fail(ent);
            return;
        }

        _scanner.SelectOrder(ent.Owner, args.Id, args.Actor, ent.Comp.ExperimentGroup, ent.Comp.VisibleOrders);
        PushState(ent);
    }

    private void OnAbandoned(Entity<ExperimentFloorScannerComponent> ent, ref ExperimentAbandonOrderMessage args)
    {
        _scanner.AbandonOrder(ent.Owner, args.Actor, ent.Comp.ExperimentGroup, ent.Comp.VisibleOrders);
        PushState(ent);
    }

    private void OnSkipped(Entity<ExperimentFloorScannerComponent> ent, ref ExperimentSkipOrderMessage args)
    {
        _scanner.SkipOrder(ent.Owner, args.Id, args.Actor, ent.Comp.ExperimentGroup, ent.Comp.VisibleOrders);
        PushState(ent);
    }

    private void OnPerform(Entity<ExperimentFloorScannerComponent> ent, ref ExperimentFloorScannerPerformMessage args)
    {
        if (ent.Comp.IsProcessing)
        {
            Fail(ent);
            return;
        }

        var database = EnsureComp<ExperimentScannerDatabaseComponent>(ent);
        if (database.ActiveOrder == null
            || !_scanner.TryGetStationDatabase(ent.Owner, out var station, out var stationDatabase))
        {
            Fail(ent);
            PushState(ent);
            return;
        }

        var targets = CollectTargets(ent);
        if (targets.Count == 0)
        {
            Fail(ent);
            PushState(ent);
            return;
        }

        var container = _containers.EnsureContainer<Container>(ent, ExperimentFloorScannerComponent.ContainerId);
        var queued = new List<EntityUid>();

        foreach (var target in targets)
        {
            if (HasComp<ItemComponent>(target))
            {
                if (_containers.Insert(target, container))
                    queued.Add(target);
                continue;
            }

            if (!TerminatingOrDeleted(target))
                queued.Add(target);
        }

        if (queued.Count == 0)
        {
            Fail(ent);
            PushState(ent);
            return;
        }

        ent.Comp.IsProcessing = true;
        SetVisual(ent, ExperimentFloorScannerVisualState.Down);
        PushState(ent);

        Timer.Spawn(ent.Comp.CapsuleStepDuration, () =>
        {
            if (!TerminatingOrDeleted(ent) && ent.Comp.IsProcessing)
                SetVisual(ent, ExperimentFloorScannerVisualState.Scanning);
        });

        ScanNext(ent, station, stationDatabase, database, queued, 0, args.Actor);
    }

    private void ScanNext(
        Entity<ExperimentFloorScannerComponent> ent,
        EntityUid station,
        ExperimentStationDatabaseComponent stationDb,
        ExperimentScannerDatabaseComponent scannerDb,
        List<EntityUid> targets,
        int index,
        EntityUid? user)
    {
        if (index >= targets.Count || !ent.Comp.IsProcessing || scannerDb.ActiveOrder == null)
        {
            Finish(ent);
            return;
        }

        var target = targets[index];
        if (!TerminatingOrDeleted(target) && _scanner.TryProcessScan(station, scannerDb.ActiveOrder, target))
        {
            if (scannerDb.ActiveOrder.ProgressCurrent >= scannerDb.ActiveOrder.ProgressTarget)
            {
                _scanner.CompleteOrder(
                    ent.Owner,
                    user,
                    station,
                    stationDb,
                    scannerDb.ActiveOrder,
                    ent.Comp.CompleteSound,
                    ent.Comp.AnnouncementChannel);

                _scanner.FillFloorAvailableOrders(station, ent.Comp.ExperimentGroup, ent.Comp.VisibleOrders, stationDb);
                Finish(ent);
                return;
            }

            _audio.PlayPvs(ent.Comp.ProgressSound, ent, ent.Comp.AudioParams);
        }

        Timer.Spawn(ent.Comp.ItemProcessDelay,
            () => ScanNext(ent, station, stationDb, scannerDb, targets, index + 1, user));
    }

    private void Finish(Entity<ExperimentFloorScannerComponent> ent)
    {
        ent.Comp.IsProcessing = false;
        SetVisual(ent, ExperimentFloorScannerVisualState.Up);

        Timer.Spawn(ent.Comp.CapsuleStepDuration, () =>
        {
            if (TerminatingOrDeleted(ent) || ent.Comp.IsProcessing)
                return;

            var container = _containers.EnsureContainer<Container>(ent, ExperimentFloorScannerComponent.ContainerId);
            _containers.EmptyContainer(container, true, Transform(ent).Coordinates);
            SetVisual(ent, ExperimentFloorScannerVisualState.Idle);
            PushState(ent);
        });
    }

    private List<EntityUid> CollectTargets(Entity<ExperimentFloorScannerComponent> ent)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid
            || !TryComp<MapGridComponent>(gridUid, out var grid)
            || !_maps.TryGetTileRef(gridUid, grid, _maps.TileIndicesFor(gridUid, grid, xform.Coordinates), out var tile))
            return new List<EntityUid>();

        return _lookup.GetLocalEntitiesIntersecting(tile, 0f)
            .Where(uid => uid != ent.Owner && !_containers.TryGetContainingContainer(uid, out _))
            .Distinct()
            .Where(uid => IsValidTarget(uid))
            .ToList();
    }

    private bool IsValidTarget(EntityUid uid)
    {
        if (HasComp<ItemComponent>(uid)
            && !HasComp<ResearchClientComponent>(uid)
            && !_tags.HasTag(uid, PipeTag))
            return true;

        return HasComp<MobStateComponent>(uid) || HasComp<PuddleComponent>(uid);
    }

    private void SetVisual(Entity<ExperimentFloorScannerComponent> ent, ExperimentFloorScannerVisualState state)
    {
        _appearance.SetData(ent.Owner, ExperimentFloorScannerVisuals.State, state);
    }

    private void PushState(Entity<ExperimentFloorScannerComponent> ent)
    {
        _scanner.PopulateFloorScannerState(ent.Owner, ent.Comp, out var state);
        _ui.SetUiState(ent.Owner, ExperimentFloorScannerUiKey.Key, state);
    }

    private void Fail(Entity<ExperimentFloorScannerComponent> ent)
    {
        _audio.PlayPvs(ent.Comp.FailureSound, ent, ent.Comp.AudioParams);
    }
}
