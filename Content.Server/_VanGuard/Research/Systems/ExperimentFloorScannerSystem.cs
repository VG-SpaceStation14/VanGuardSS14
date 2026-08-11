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
/// Floor-mounted experiment scanner. Instead of scanning a single target like the
/// handheld scanner, it sweeps every loose item standing on its own tile and feeds
/// them into the active order's condition one by one, with a visual scan animation.
/// </summary>
public sealed partial class ExperimentFloorScannerSystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> PipeTag = "Pipe";

    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ExperimentScannerSystem _experimentScanner = default!;
    [Dependency] private TagSystem _tag = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExperimentFloorScannerComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<ExperimentFloorScannerComponent, ExperimentSelectOrderMessage>(OnOrderSelected);
        SubscribeLocalEvent<ExperimentFloorScannerComponent, ExperimentAbandonOrderMessage>(OnOrderAbandoned);
        SubscribeLocalEvent<ExperimentFloorScannerComponent, ExperimentSkipOrderMessage>(OnOrderSkipped);
        SubscribeLocalEvent<ExperimentFloorScannerComponent, ExperimentFloorScannerPerformMessage>(OnPerform);
        SubscribeLocalEvent<ExperimentFloorScannerComponent, ResearchRegistrationChangedEvent>(OnRegistrationChanged);
        SubscribeLocalEvent<ExperimentFloorScannerComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<ExperimentFloorScannerComponent> ent, ref ComponentStartup args)
    {
        _container.EnsureContainer<Container>(ent, ExperimentFloorScannerComponent.ContainerId);
        UpdateAppearance(ent, ExperimentFloorScannerVisualState.Idle);

        UpdateUi(ent);
    }

    private void OnUiOpened(Entity<ExperimentFloorScannerComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnRegistrationChanged(Entity<ExperimentFloorScannerComponent> ent, ref ResearchRegistrationChangedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnOrderSelected(Entity<ExperimentFloorScannerComponent> ent, ref ExperimentSelectOrderMessage args)
    {
        if (!_experimentScanner.IsFloorCompatibleOrder(args.Id, ent.Owner))
        {
            _audio.PlayPvs(ent.Comp.FailureSound, ent, ent.Comp.AudioParams);
            return;
        }

        _experimentScanner.SelectOrder(ent.Owner, args.Id, args.Actor, ent.Comp.ExperimentGroup, ent.Comp.VisibleOrders);
        UpdateUi(ent);
    }

    private void OnOrderAbandoned(Entity<ExperimentFloorScannerComponent> ent, ref ExperimentAbandonOrderMessage args)
    {
        _experimentScanner.AbandonOrder(ent.Owner, args.Actor, ent.Comp.ExperimentGroup, ent.Comp.VisibleOrders);
        UpdateUi(ent);
    }

    private void OnOrderSkipped(Entity<ExperimentFloorScannerComponent> ent, ref ExperimentSkipOrderMessage args)
    {
        _experimentScanner.SkipOrder(ent.Owner, args.Id, args.Actor, ent.Comp.ExperimentGroup, ent.Comp.VisibleOrders);
        UpdateUi(ent);
    }

    private void OnPerform(Entity<ExperimentFloorScannerComponent> ent, ref ExperimentFloorScannerPerformMessage args)
    {
        if (ent.Comp.IsProcessing)
        {
            _audio.PlayPvs(ent.Comp.FailureSound, ent, ent.Comp.AudioParams);
            return;
        }

        var scannerDb = EnsureComp<ExperimentScannerDatabaseComponent>(ent);
        if (scannerDb.ActiveOrder == null)
        {
            _audio.PlayPvs(ent.Comp.FailureSound, ent, ent.Comp.AudioParams);
            UpdateUi(ent);
            return;
        }

        if (!_experimentScanner.TryGetStationDatabase(ent.Owner, out var station, out var stationDb))
        {
            _audio.PlayPvs(ent.Comp.FailureSound, ent, ent.Comp.AudioParams);
            UpdateUi(ent);
            return;
        }

        var (items, inPlaceTargets) = GetScanTargetsOnTile(ent);
        if (items.Count == 0 && inPlaceTargets.Count == 0)
        {
            _audio.PlayPvs(ent.Comp.FailureSound, ent, ent.Comp.AudioParams);
            UpdateUi(ent);
            return;
        }

        var itemContainer = _container.EnsureContainer<Container>(ent, ExperimentFloorScannerComponent.ContainerId);
        var scannedItems = new List<EntityUid>();
        foreach (var item in items)
        {
            if (_container.Insert(item, itemContainer))
                scannedItems.Add(item);
        }

        // Creatures and puddles cannot be picked up - they are scanned in place.
        foreach (var target in inPlaceTargets)
        {
            if (!TerminatingOrDeleted(target))
                scannedItems.Add(target);
        }

        if (scannedItems.Count == 0)
        {
            _audio.PlayPvs(ent.Comp.FailureSound, ent, ent.Comp.AudioParams);
            UpdateUi(ent);
            return;
        }

        var actor = args.Actor;
        ent.Comp.IsProcessing = true;
        UpdateAppearance(ent, ExperimentFloorScannerVisualState.Down);
        UpdateUi(ent);

        Timer.Spawn(ent.Comp.CapsuleStepDuration,
            () =>
            {
                if (TerminatingOrDeleted(ent) || !ent.Comp.IsProcessing)
                    return;
                UpdateAppearance(ent, ExperimentFloorScannerVisualState.Scanning);
            });

        ProcessItemBatch(ent, station, stationDb, scannerDb, scannedItems, 0, actor);
    }


    private void ProcessItemBatch(
        Entity<ExperimentFloorScannerComponent> ent,
        EntityUid station,
        ExperimentStationDatabaseComponent stationDb,
        ExperimentScannerDatabaseComponent scannerDb,
        List<EntityUid> items,
        int index,
        EntityUid? user)
    {
        if (index >= items.Count || !ent.Comp.IsProcessing || scannerDb.ActiveOrder == null)
        {
            FinishScan(ent, items, user);
            return;
        }

        var item = items[index];
        if (!TerminatingOrDeleted(item))
        {
            var order = scannerDb.ActiveOrder;
            if (_experimentScanner.TryProcessScan(station, order, item))
            {
                if (order.ProgressCurrent >= order.ProgressTarget)
                {
                    _experimentScanner.CompleteOrder(
                        ent.Owner,
                        user,
                        station,
                        stationDb,
                        order,
                        ent.Comp.CompleteSound,
                        ent.Comp.AnnouncementChannel);

                    _experimentScanner.FillFloorAvailableOrders(station, ent.Comp.ExperimentGroup, ent.Comp.VisibleOrders, stationDb);
                    FinishScan(ent, items, user);
                    return;
                }

                _audio.PlayPvs(ent.Comp.ProgressSound, ent, ent.Comp.AudioParams);
            }
        }

        Timer.Spawn(ent.Comp.ItemProcessDelay,
            () => ProcessItemBatch(ent, station, stationDb, scannerDb, items, index + 1, user));
    }

    private void FinishScan(Entity<ExperimentFloorScannerComponent> ent, List<EntityUid> scannedItems, EntityUid? user)
    {
        ent.Comp.IsProcessing = false;
        UpdateAppearance(ent, ExperimentFloorScannerVisualState.Up);

        Timer.Spawn(ent.Comp.CapsuleStepDuration,
            () =>
            {
                if (TerminatingOrDeleted(ent) || ent.Comp.IsProcessing)
                    return;

                var itemContainer = _container.EnsureContainer<Container>(ent, ExperimentFloorScannerComponent.ContainerId);
                _container.EmptyContainer(itemContainer, true, Transform(ent).Coordinates);
                UpdateAppearance(ent, ExperimentFloorScannerVisualState.Idle);
                UpdateUi(ent);
            });
    }

    private (List<EntityUid> items, List<EntityUid> inPlaceTargets) GetScanTargetsOnTile(Entity<ExperimentFloorScannerComponent> ent)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var gridComp))
            return (new List<EntityUid>(), new List<EntityUid>());

        var tileIndices = _maps.TileIndicesFor(gridUid, gridComp, xform.Coordinates);
        if (!_maps.TryGetTileRef(gridUid, gridComp, tileIndices, out var tileRef))
            return (new List<EntityUid>(), new List<EntityUid>());

        var items = new List<EntityUid>();
        var inPlaceTargets = new List<EntityUid>();

        var candidates = _lookup.GetLocalEntitiesIntersecting(tileRef, 0f)
            .Where(uid => uid != ent.Owner
                          && !_container.TryGetContainingContainer(uid, out _))
            .Distinct();

        foreach (var uid in candidates)
        {
            if (HasComp<ItemComponent>(uid)
                && !HasComp<ResearchClientComponent>(uid)
                && !_tag.HasTag(uid, PipeTag))
            {
                items.Add(uid);
            }
            // Living (or dead) creatures and floor puddles are scanned in place -
            // this lets the machine handle reagent-in-body and puddle experiments.
            else if (HasComp<MobStateComponent>(uid) || HasComp<PuddleComponent>(uid))
            {
                inPlaceTargets.Add(uid);
            }
        }

        return (items, inPlaceTargets);
    }

    private void UpdateAppearance(Entity<ExperimentFloorScannerComponent> ent, ExperimentFloorScannerVisualState state)
    {
        _appearance.SetData(ent.Owner, ExperimentFloorScannerVisuals.State, state);
    }

    private void UpdateUi(Entity<ExperimentFloorScannerComponent> ent)
    {
        _experimentScanner.PopulateFloorScannerState(ent.Owner, ent.Comp, out var state);
        _ui.SetUiState(ent.Owner, ExperimentFloorScannerUiKey.Key, state);
    }
}

