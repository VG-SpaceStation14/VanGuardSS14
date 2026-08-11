using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Ame;
using Content.Server.Ame.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Radio.EntitySystems;
using Content.Server.Radiation.Components;
using Content.Server.Research.Disk;
using Content.Server.Research.Systems;
using Content.Server.Station.Systems;
using Content.Server._VanGuard.Research.Components;
using Content.Shared._VanGuard.Research.Components;
using Content.Shared._VanGuard.Research.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mech.Components;
using Content.Shared.NodeContainer;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Research.Components;
using Content.Shared.Tag;
using Content.Shared.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Research.Systems;

/// <summary>
/// Shared core for the handheld experiment scanner and the floor experiment scanner.
/// Manages the station-wide experiment order database, accepts orders on individual
/// scanners, validates scanned entities against the active order's condition and
/// pays out research points on completion.
/// </summary>
public sealed partial class ExperimentScannerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ResearchSystem _research = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExperimentScannerComponent, BoundUIOpenedEvent>(OnScannerUiOpened);
        SubscribeLocalEvent<ExperimentScannerComponent, ExperimentSelectOrderMessage>(OnScannerOrderSelected);
        SubscribeLocalEvent<ExperimentScannerComponent, ExperimentAbandonOrderMessage>(OnScannerOrderAbandoned);
        SubscribeLocalEvent<ExperimentScannerComponent, ExperimentSkipOrderMessage>(OnScannerOrderSkipped);
        SubscribeLocalEvent<ExperimentScannerComponent, AfterInteractEvent>(OnScannerAfterInteract);
        SubscribeLocalEvent<MetaDataComponent, InteractUsingEvent>(OnScannerInteractUsing);
    }

    #region Handheld Scanner Events

    private void OnScannerUiOpened(Entity<ExperimentScannerComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!TryGetStationDatabase(ent.Owner, out var station, out var stationDb))
            return;

        var scannerDb = EnsureComp<ExperimentScannerDatabaseComponent>(ent);
        FillAvailableOrders(station, ent.Comp.ExperimentGroup, ent.Comp.VisibleOrders, stationDb);
        UpdateScannerUi(ent, stationDb, scannerDb);
    }

    private void OnScannerOrderSelected(Entity<ExperimentScannerComponent> ent, ref ExperimentSelectOrderMessage args)
    {
        SelectOrder(ent.Owner, args.Id, args.Actor, ent.Comp.ExperimentGroup, ent.Comp.VisibleOrders, ent.Comp.SelectSound);
        UpdateScannerUi(ent);
    }

    private void OnScannerOrderAbandoned(Entity<ExperimentScannerComponent> ent, ref ExperimentAbandonOrderMessage args)
    {
        AbandonOrder(ent.Owner, args.Actor, ent.Comp.ExperimentGroup, ent.Comp.VisibleOrders, ent.Comp.SelectSound);
        UpdateScannerUi(ent);
    }

    private void OnScannerOrderSkipped(Entity<ExperimentScannerComponent> ent, ref ExperimentSkipOrderMessage args)
    {
        SkipOrder(ent.Owner, args.Id, args.Actor, ent.Comp.ExperimentGroup, ent.Comp.VisibleOrders, ent.Comp.SkipSound);
        UpdateScannerUi(ent);
    }

    private void OnScannerAfterInteract(Entity<ExperimentScannerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Target is not { } target || !args.CanReach)
            return;

        TryScanTargetHandheld(ent, args.User, target);
    }

    private void OnScannerInteractUsing(Entity<MetaDataComponent> ent, ref InteractUsingEvent args)
    {
        if (!TryComp<ExperimentScannerComponent>(args.Used, out var scannerComp))
            return;

        var scanner = (args.Used, scannerComp);
        TryScanTargetHandheld(scanner, args.User, args.Target);
    }

    private void TryScanTargetHandheld(Entity<ExperimentScannerComponent> ent, EntityUid user, EntityUid target)
    {
        if (!TryComp(ent, out ExperimentScannerDatabaseComponent? db) || db.ActiveOrder == null)
            return;

        if (!TryGetStationDatabase(ent.Owner, out var station, out var stationDb))
        {
            Deny(ent.Owner, user, "experiment-scanner-popup-no-station", ent.Comp.DenySound);
            return;
        }

        if (!TryProcessScan(station, db.ActiveOrder, target))
            return;

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):user} advanced experiment order [id:{db.ActiveOrder.Id}, prototype:{db.ActiveOrder.Prototype}, progress:{db.ActiveOrder.ProgressCurrent}/{db.ActiveOrder.ProgressTarget}] by scanning {ToPrettyString(target):entity} with scanner {ToPrettyString(ent):entity}");

        if (db.ActiveOrder.ProgressCurrent < db.ActiveOrder.ProgressTarget)
        {
            _popup.PopupClient(Loc.GetString("experiment-scanner-progress-popup",
                ("current", db.ActiveOrder.ProgressCurrent),
                ("target", db.ActiveOrder.ProgressTarget)), user, user);
            _audio.PlayPvs(ent.Comp.ProgressSound, ent);
            UpdateScannerUi(ent, stationDb, db);
            return;
        }

        CompleteOrder(ent.Owner, user, station, stationDb, db.ActiveOrder, ent.Comp.CompleteSound, ent.Comp.AnnouncementChannel);
        FillAvailableOrders(station, ent.Comp.ExperimentGroup, ent.Comp.VisibleOrders, stationDb);
        UpdateScannerUi(ent, stationDb, db);
    }

    private void UpdateScannerUi(
        Entity<ExperimentScannerComponent> scanner,
        ExperimentStationDatabaseComponent? stationDb = null,
        ExperimentScannerDatabaseComponent? scannerDb = null)
    {
        if (!TryGetStationDatabase(scanner.Owner, out var station, out var resolvedStationDb))
            return;

        stationDb ??= resolvedStationDb;
        scannerDb ??= EnsureComp<ExperimentScannerDatabaseComponent>(scanner);

        var state = BuildScannerState(scanner.Owner, scannerDb, stationDb, scanner.Comp.ExperimentGroup, scanner.Comp.VisibleOrders);
        _ui.SetUiState(scanner.Owner, ExperimentScannerUiKey.Key, state);
    }

    #endregion

    #region Public API for Floor Scanner

    /// <summary>
    /// Resolves the station this scanner is attached to (and its shared order database).
    /// Scanners remember their last linked station even after leaving it.
    /// </summary>
    public bool TryGetStationDatabase(EntityUid scanner, out EntityUid station, out ExperimentStationDatabaseComponent stationDb)
    {
        station = default;
        stationDb = default!;

        var scannerDb = EnsureComp<ExperimentScannerDatabaseComponent>(scanner);
        if (_station.GetOwningStation(scanner) is { } owningStation)
        {
            scannerDb.LinkedStation = owningStation;
            station = owningStation;
            stationDb = EnsureComp<ExperimentStationDatabaseComponent>(owningStation);
            return true;
        }

        if (scannerDb.LinkedStation is { } linkedStation &&
            TryComp<ExperimentStationDatabaseComponent>(linkedStation, out var linkedDb))
        {
            station = linkedStation;
            stationDb = linkedDb;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Takes an available order from the station database and marks it active on the scanner.
    /// </summary>
    public void SelectOrder(
        EntityUid scanner,
        string orderId,
        EntityUid? user,
        string experimentGroup,
        int visibleOrders,
        SoundSpecifier? selectSound = null)
    {
        if (!TryComp(scanner, out ExperimentScannerDatabaseComponent? db))
            return;

        if (!TryGetStationDatabase(scanner, out var station, out var stationDb))
        {
            Deny(scanner, user, "experiment-scanner-popup-no-station");
            return;
        }

        if (db.ActiveOrder != null)
        {
            Deny(scanner, user, "experiment-scanner-popup-already-active");
            return;
        }

        for (var i = 0; i < stationDb.AvailableOrders.Count; i++)
        {
            if (stationDb.AvailableOrders[i].Id != orderId)
                continue;

            db.ActiveOrder = stationDb.AvailableOrders[i];
            db.ActiveOrder.HadServerOnAccept = TryGetAssignedServer(scanner, out _, out _);
            stationDb.AvailableOrders.RemoveAt(i);

            var sound = selectSound ?? new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");
            _audio.PlayPvs(sound, scanner);

            if (user is { Valid: true } validUser)
            {
                _popup.PopupClient(Loc.GetString("experiment-scanner-popup-selected"), validUser, validUser);
                _adminLogger.Add(LogType.Action, LogImpact.Low,
                    $"{ToPrettyString(validUser):user} accepted experiment order [id:{db.ActiveOrder.Id}, prototype:{db.ActiveOrder.Prototype}] with scanner {ToPrettyString(scanner):entity}");
            }
            break;
        }

        UpdateAllUIs(scanner, stationDb, db, experimentGroup, visibleOrders);
    }

    /// <summary>
    /// Returns the active order back to the available pool.
    /// </summary>
    public void AbandonOrder(
        EntityUid scanner,
        EntityUid? user,
        string experimentGroup,
        int visibleOrders,
        SoundSpecifier? selectSound = null)
    {
        if (!TryComp(scanner, out ExperimentScannerDatabaseComponent? db))
            return;

        if (!TryGetStationDatabase(scanner, out var station, out var stationDb))
        {
            Deny(scanner, user, "experiment-scanner-popup-no-station");
            return;
        }

        if (db.ActiveOrder == null)
        {
            Deny(scanner, user, "experiment-scanner-popup-no-active");
            return;
        }

        var abandonedOrder = db.ActiveOrder;
        stationDb.AvailableOrders.Add(abandonedOrder);
        db.ActiveOrder = null;

        var sound = selectSound ?? new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");
        _audio.PlayPvs(sound, scanner);

        if (user is { Valid: true } validUser)
        {
            _popup.PopupClient(Loc.GetString("experiment-scanner-popup-abandoned"), validUser, validUser);
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(validUser):user} abandoned experiment order [id:{abandonedOrder.Id}, prototype:{abandonedOrder.Prototype}] with scanner {ToPrettyString(scanner):entity}");
        }

        UpdateAllUIs(scanner, stationDb, db, experimentGroup, visibleOrders);
    }

    /// <summary>
    /// Discards the given available order and replaces it with a new random one.
    /// Cooldown-limited per scanner.
    /// </summary>
    public void SkipOrder(
        EntityUid scanner,
        string orderId,
        EntityUid? user,
        string experimentGroup,
        int visibleOrders,
        SoundSpecifier? skipSound = null)
    {
        if (!TryComp(scanner, out ExperimentScannerDatabaseComponent? db))
            return;

        if (!TryGetStationDatabase(scanner, out var station, out var stationDb))
        {
            Deny(scanner, user, "experiment-scanner-popup-no-station");
            return;
        }

        if (_timing.CurTime < db.NextSkipTime)
        {
            Deny(scanner, user, "experiment-scanner-popup-skip-cooldown");
            return;
        }

        var index = stationDb.AvailableOrders.FindIndex(o => o.Id == orderId);
        if (index < 0)
        {
            Deny(scanner, user, "experiment-scanner-popup-no-available");
            return;
        }

        var removed = stationDb.AvailableOrders[index];
        stationDb.AvailableOrders.RemoveAt(index);

        if (!TryAddOrder(station, experimentGroup, stationDb, removed.Prototype))
        {
            stationDb.AvailableOrders.Insert(index, removed);
            Deny(scanner, user, "experiment-scanner-popup-no-available");
            return;
        }

        db.NextSkipTime = _timing.CurTime + db.SkipDelay;

        var sound = skipSound ?? new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
        _audio.PlayPvs(sound, scanner);

        if (user is { Valid: true } validUser)
        {
            _popup.PopupClient(Loc.GetString("experiment-scanner-popup-skipped"), validUser, validUser);
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(validUser):user} skipped experiment order [id:{removed.Id}, prototype:{removed.Prototype}] with scanner {ToPrettyString(scanner):entity}");
        }

        UpdateAllUIs(scanner, stationDb, db, experimentGroup, visibleOrders);
    }

    /// <summary>
    /// Tries to advance an active order by scanning the given entity.
    /// Returns true when the scan was accepted.
    /// </summary>
    public bool TryProcessScan(EntityUid station, StationExperimentOrderData order, EntityUid target)
    {
        return TryProcessScanInternal(station, order, target);
    }


    /// <summary>
    /// Pays out the reward for a finished order: either adds points to the linked
    /// research server or spawns a research disk when no server was linked when the
    /// order was accepted. Announces the completion over the science radio channel.
    /// </summary>
    public void CompleteOrder(
        EntityUid scanner,
        EntityUid? user,
        EntityUid station,
        ExperimentStationDatabaseComponent stationDb,
        StationExperimentOrderData order,
        SoundSpecifier? completeSound = null,
        ProtoId<RadioChannelPrototype>? channel = null)
    {
        var proto = _proto.Index(order.Prototype);

        if (TryGetAssignedServer(scanner, out var server, out var serverComp))
        {
            _research.ModifyServerPoints(server, proto.RewardPoints, serverComp);
            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(user):user} completed experiment order [id:{order.Id}, prototype:{order.Prototype}] and awarded {proto.RewardPoints} points to research server {ToPrettyString(server):entity} using scanner {ToPrettyString(scanner):entity}");
        }
        else if (!order.HadServerOnAccept)
        {
            var disk = Spawn("ResearchDisk", Transform(scanner).Coordinates);
            if (TryComp<ResearchDiskComponent>(disk, out var diskComp))
                diskComp.Points = proto.RewardPoints;
            if (user is { Valid: true } validUser)
                _popup.PopupClient(Loc.GetString("experiment-scanner-disk-fallback-popup", ("points", proto.RewardPoints)), validUser, validUser);
            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(user):user} completed experiment order [id:{order.Id}, prototype:{order.Prototype}] without server link; spawned fallback research disk {ToPrettyString(disk):entity} with {proto.RewardPoints} points using scanner {ToPrettyString(scanner):entity}");
        }
        else
        {
            if (user is { Valid: true } validUser)
                _popup.PopupClient(Loc.GetString("experiment-scanner-popup-no-server"), validUser, validUser);
            return;
        }

        if (user is { Valid: true } validUser2)
        {
            _popup.PopupClient(Loc.GetString("experiment-scanner-complete-popup"), validUser2, validUser2);
        }

        var identityInfo = new TryGetIdentityShortInfoEvent(scanner, user.GetValueOrDefault(), true);
        RaiseLocalEvent(identityInfo);
        var performer = identityInfo.Title ?? Loc.GetString("experiment-scanner-complete-radio-unknown");
        var message = Loc.GetString("experiment-scanner-complete-radio-broadcast",
            ("order", Loc.GetString(proto.Name)),
            ("performer", performer),
            ("points", proto.RewardPoints));

        var radioChannel = channel ?? new ProtoId<RadioChannelPrototype>("Science");
        _radio.SendRadioMessage(scanner, message, radioChannel, scanner, escapeMarkup: false);

        var sound = completeSound ?? new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
        _audio.PlayPvs(sound, scanner);

        stationDb.UsedOrders.Add(order.Prototype);

        var db = EnsureComp<ExperimentScannerDatabaseComponent>(scanner);
        db.ActiveOrder = null;
    }


    /// <summary>
    /// Refills the available pool up to the desired count with fresh random orders.
    /// When a filter is supplied, only matching experiments are added.
    /// </summary>
    public void FillAvailableOrders(EntityUid station, string experimentGroup, int visibleOrders, ExperimentStationDatabaseComponent db, Func<ResearchExperimentPrototype, bool>? filter = null)
    {
        while (db.AvailableOrders.Count < visibleOrders)
        {
            if (!TryAddOrder(station, experimentGroup, db, filter: filter))
                break;
        }
    }

    /// <summary>Builds the UI state for a handheld scanner.</summary>
    public ExperimentScannerState BuildScannerState(
        EntityUid scanner,
        ExperimentScannerDatabaseComponent scannerDb,
        ExperimentStationDatabaseComponent? stationDb,
        string experimentGroup,
        int visibleOrders)
    {
        if (stationDb == null)
            return new ExperimentScannerState(new List<ExperimentOrderUiData>(), null, TimeSpan.Zero, false, null);

        FillAvailableOrders(scanner, experimentGroup, visibleOrders, stationDb);
        return BuildScannerStateInternal(scanner, scannerDb, stationDb);
    }

    /// <summary>
    /// Whether the floor scanner can realistically complete this experiment.
    /// The floor machine can only sweep items, creatures and puddles that fit on
    /// its tile - large machines like vending machines, AMEs or mechs are excluded.
    /// </summary>
    public bool IsFloorCompatible(ResearchExperimentPrototype proto)
        => IsFloorCompatible(proto.Condition);

    public bool IsFloorCompatible(ResearchExperimentCondition condition)
        => condition is SpeciesReagentScanCondition
            or SolutionReagentScanCondition
            or PrototypeMatchScanCondition
            or TagBatchScanCondition
            or TaggedSolutionScanCondition
            or SignatureDiversityScanCondition
            or ComponentPresenceScanCondition;

    /// <summary>
    /// Checks whether a specific order currently in the station pool can be
    /// fulfilled by a floor scanner.
    /// </summary>
    public bool IsFloorCompatibleOrder(string orderId, EntityUid scanner)
    {
        if (!TryGetStationDatabase(scanner, out _, out var stationDb))
            return false;

        var order = stationDb.AvailableOrders.FirstOrDefault(o => o.Id == orderId);
        if (order == null || !_proto.TryIndex(order.Prototype, out var proto))
            return false;

        return IsFloorCompatible(proto);
    }

    /// <summary>
    /// Tops up the available pool until it holds enough orders that a floor
    /// scanner can actually complete.
    /// </summary>
    public void FillFloorAvailableOrders(EntityUid station, string experimentGroup, int visibleOrders, ExperimentStationDatabaseComponent db)
    {
        var floorCount = db.AvailableOrders.Count(o => _proto.TryIndex(o.Prototype, out var p) && IsFloorCompatible(p));
        while (floorCount < visibleOrders)
        {
            if (!TryAddOrder(station, experimentGroup, db, filter: IsFloorCompatible))
                break;
            floorCount++;
        }
    }

    /// <summary>Builds the UI state for a floor scanner.</summary>
    public void PopulateFloorScannerState(
        EntityUid scanner,
        ExperimentFloorScannerComponent floorComp,
        out ExperimentFloorScannerState state)
    {
        if (!TryGetStationDatabase(scanner, out var station, out var stationDb))
        {
            state = new ExperimentFloorScannerState(new List<ExperimentOrderUiData>(), null, TimeSpan.Zero, false, null, floorComp.IsProcessing);
            return;
        }

        var scannerDb = EnsureComp<ExperimentScannerDatabaseComponent>(scanner);
        FillFloorAvailableOrders(station, floorComp.ExperimentGroup, floorComp.VisibleOrders, stationDb);

        var available = stationDb.AvailableOrders
            .Where(o => _proto.TryIndex(o.Prototype, out var p) && IsFloorCompatible(p))
            .Select(ToUiData)
            .ToList();
        var active = scannerDb.ActiveOrder == null ? null : ToUiData(scannerDb.ActiveOrder);
        var untilNextSkip = scannerDb.NextSkipTime - _timing.CurTime;

        string? serverName = null;
        var hasServer = false;
        if (TryComp<ResearchClientComponent>(scanner, out var client) && client.Server is { } server && TryComp<ResearchServerComponent>(server, out var serverComp))
        {
            hasServer = true;
            serverName = serverComp.ServerName;
        }

        state = new ExperimentFloorScannerState(available, active, untilNextSkip, hasServer, serverName, floorComp.IsProcessing);
    }

    #endregion


    #region Private Helpers

    private bool TryProcessScanInternal(EntityUid station, StationExperimentOrderData order, EntityUid target)
    {
        var proto = _proto.Index(order.Prototype);
        switch (proto.Condition)
        {
            case AmeOverloadScanCondition ame:
                if (!TryComp<AmeControllerComponent>(target, out var controller))
                    break;
                if (!TryGetAmeCoreCount(target, out var coreCount) || coreCount <= 0)
                    break;
                if (ame.RequirePowered && !this.IsPowered(target, EntityManager))
                    break;
                if (controller.InjectionAmount > ame.SafeInjectionPerCore * coreCount)
                {
                    order.ProgressCurrent = 1;
                    return true;
                }
                break;

            case SpeciesReagentScanCondition species:
                if (order.SelectedSpecies == null || order.SelectedReagent == null)
                    return false;
                if (!TryComp<HumanoidProfileComponent>(target, out var hum) ||
                    hum.Species != order.SelectedSpecies)
                    return false;
                if (!_solution.TryGetSolution(target, species.SolutionName, out _, out var chemicalSolution))
                    return false;
                if (!chemicalSolution.Contents.Any(r => r.Reagent.Prototype == order.SelectedReagent))
                    return false;
                order.ProgressCurrent = 1;
                return true;

            case SolutionReagentScanCondition puddle:
                if (_solution.TryGetSolution(target, puddle.SolutionName, out _, out var puddleSolution) &&
                    puddleSolution.Contents.Any(r => r.Reagent.Prototype == puddle.Reagent))
                {
                    order.ProgressCurrent = 1;
                    return true;
                }
                break;

            case FullyLoadedMechScanCondition mech:
                if (!TryComp<MechComponent>(target, out var mechComp) || !TryComp(target, out MetaDataComponent? meta))
                    return false;
                if (meta.EntityPrototype == null)
                    return false;
                if (order.SelectedPrototype != null)
                {
                    var fullMatchesSelected = meta.EntityPrototype.ID == order.SelectedPrototype;
                    var fullMatchesAlias = mech.PrototypeAliases.TryGetValue(order.SelectedPrototype, out var fullAliases) &&
                                           fullAliases.Contains(meta.EntityPrototype.ID);
                    if (!fullMatchesSelected && !fullMatchesAlias)
                        return false;
                }
                else if (!mech.AllowedPrototypes.Contains(meta.EntityPrototype.ID))
                {
                    return false;
                }
                if (mechComp.EquipmentContainer.ContainedEntities.Count >= mechComp.MaxEquipmentAmount)
                {
                    order.ProgressCurrent = 1;
                    return true;
                }
                break;

            case PrototypeMatchScanCondition matched:
                if (order.SelectedPrototype == null || !TryComp(target, out MetaDataComponent? mobMeta))
                    return false;
                if (mobMeta.EntityPrototype?.ID is not { } scannedProto)
                    return false;
                var protoMatchesSelected = scannedProto == order.SelectedPrototype;
                var protoMatchesAlias = matched.PrototypeAliases.TryGetValue(order.SelectedPrototype, out var protoAliases) &&
                                        protoAliases.Contains(scannedProto);
                if (protoMatchesSelected || protoMatchesAlias)
                {
                    order.ProgressCurrent = 1;
                    return true;
                }
                break;

            case DelayedRescanScanCondition vending:
                if (order.SelectedPrototype == null || !TryComp(target, out MetaDataComponent? vendMeta))
                    return false;
                if (vendMeta.EntityPrototype?.ID != order.SelectedPrototype)
                    return false;
                if (order.ProgressCurrent == 0)
                {
                    order.ProgressCurrent = 1;
                    order.SelectedEntity = target;
                    order.RescanAfter = _timing.CurTime + vending.RescanDelay;
                    return true;
                }
                if (order.SelectedEntity == null || order.SelectedEntity != target)
                    return false;
                if (_timing.CurTime >= order.RescanAfter)
                {
                    order.ProgressCurrent = 2;
                    return true;
                }
                break;

            case TagBatchScanCondition batch:
                if (!PassesTags(target, batch.RequiredTags, batch.ForbiddenTags))
                    return false;
                if (batch.RequiredComponents.Count > 0 &&
                    !PassesRequiredComponents(target, batch.RequiredComponents))
                    return false;
                if (order.ScannedEntities.Contains(target))
                    return false;
                order.ScannedEntities.Add(target);
                if (order.ProgressCurrent < order.ProgressTarget)
                    order.ProgressCurrent++;
                return true;

            case ComponentPresenceScanCondition present:
                if (PassesRequiredComponents(target, present.RequiredComponents))
                {
                    order.ProgressCurrent = 1;
                    return true;
                }
                break;

            case TaggedSolutionScanCondition baton:
                if (!PassesTags(target, baton.RequiredTags, baton.ForbiddenTags) ||
                    (baton.RequiredComponents.Count > 0 &&
                     !PassesRequiredComponents(target, baton.RequiredComponents)) ||
                    !_solution.TryGetSolution(target, baton.SolutionName, out _, out var batSolution))
                    return false;
                var amount = batSolution.Contents
                    .Where(r => r.Reagent.Prototype == baton.Reagent)
                    .Sum(r => (float) r.Quantity.Float());
                if (amount >= baton.Quantity)
                {
                    order.ProgressCurrent = 1;
                    return true;
                }
                break;

            case RadiationExposureScanCondition rad:
                if (!PassesRequiredComponents(target, rad.RequiredComponents) ||
                    !TryComp<RadiationReceiverComponent>(target, out var receiver))
                    return false;
                if (receiver.CurrentRadiation >= rad.MinRadiation)
                {
                    order.ProgressCurrent = 1;
                    return true;
                }
                break;

            case CanisterGasScanCondition gas:
                if (order.SelectedReagent == null ||
                    !PassesRequiredComponents(target, gas.RequiredComponents) ||
                    !TryComp<GasCanisterComponent>(target, out var canister) ||
                    !Enum.TryParse<Gas>(order.SelectedReagent, true, out var targetGas))
                    return false;
                if (canister.Air.GetMoles(targetGas) >= gas.MinMoles)
                {
                    order.ProgressCurrent = 1;
                    return true;
                }
                break;

            case SignatureDiversityScanCondition sig:
                if (!PassesRequiredComponents(target, sig.RequiredComponents) ||
                    !TryComp<PaperComponent>(target, out var paper))
                    return false;
                var unique = paper.StampedBy
                    .Select(s => s.StampedName.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                if (unique >= sig.MinUniqueSignatures)
                {
                    order.ProgressCurrent = 1;
                    return true;
                }
                break;

            case PoweredStateScanCondition powered:
                if (!PassesRequiredComponents(target, powered.RequiredComponents))
                    return false;
                if (powered.RequirePowered && !this.IsPowered(target, EntityManager))
                    return false;
                if (powered.RequireGravityActive &&
                    (!TryComp<GravityGeneratorComponent>(target, out var gravity) || !gravity.GravityActive))
                    return false;
                order.ProgressCurrent = 1;
                return true;
        }

        return false;
    }


    /// <summary>
    /// Picks a random unused experiment prototype from the requested group and adds
    /// it to the available pool.
    /// </summary>
    private bool TryAddOrder(EntityUid station, string experimentGroup, ExperimentStationDatabaseComponent db, string? excludedPrototype = null, Func<ResearchExperimentPrototype, bool>? filter = null)
    {
        var candidates = _proto.EnumeratePrototypes<ResearchExperimentPrototype>()
            .Where(p => p.Group == experimentGroup
                && !db.UsedOrders.Contains(p.ID)
                && !db.AvailableOrders.Any(o => o.Prototype == p.ID)
                && p.ID != excludedPrototype
                && (filter == null || filter(p)))
            .ToList();

        if (candidates.Count == 0)
            return false;

        var picked = _random.Pick(candidates);
        if (!TryCreateOrder(station, picked, db.NextOrderId, out var order))
            return false;

        db.NextOrderId++;
        db.AvailableOrders.Add(order);
        return true;
    }

    /// <summary>
    /// Materializes an order for the given experiment prototype and pre-rolls any
    /// random target parameters (species, reagent, prototype, department, gas).
    /// </summary>
    private bool TryCreateOrder(EntityUid station, ResearchExperimentPrototype proto, int ordinal, out StationExperimentOrderData order)
    {
        order = new StationExperimentOrderData
        {
            Id = $"EXP-{ordinal:D3}",
            Prototype = proto.ID,
            ProgressCurrent = 0,
            ProgressTarget = 1
        };

        switch (proto.Condition)
        {
            case SpeciesReagentScanCondition species:
            {
                if (species.Reagents.Count == 0)
                    return false;

                var presentSpecies = new HashSet<string>();
                var query = EntityQueryEnumerator<HumanoidProfileComponent>();
                while (query.MoveNext(out var uid, out var humanoid))
                {
                    if (_station.GetOwningStation(uid) != station)
                        continue;
                    if (!_proto.TryIndex<SpeciesPrototype>(humanoid.Species, out var speciesProto) || !speciesProto.RoundStart)
                        continue;
                    if (species.ExcludedSpecies.Contains(humanoid.Species))
                        continue;
                    presentSpecies.Add(humanoid.Species);
                }

                if (presentSpecies.Count == 0)
                    return false;

                order.SelectedSpecies = _random.Pick(presentSpecies.ToList());
                order.SelectedReagent = _random.Pick(species.Reagents);
                break;
            }
            case PrototypeMatchScanCondition matched:
                if (matched.AllowedPrototypes.Count == 0)
                    return false;
                order.SelectedPrototype = _random.Pick(matched.AllowedPrototypes);
                break;
            case FullyLoadedMechScanCondition mech:
                if (mech.AllowedPrototypes.Count == 0)
                    return false;
                order.SelectedPrototype = _random.Pick(mech.AllowedPrototypes);
                break;
            case DelayedRescanScanCondition vending:
            {
                if (vending.DepartmentVendingPrototypes.Count == 0)
                    return false;
                var departments = vending.DepartmentVendingPrototypes.Keys.ToList();
                var department = _random.Pick(departments);
                var prototypes = vending.DepartmentVendingPrototypes[department];
                if (prototypes.Count == 0)
                    return false;
                order.SelectedDepartment = department;
                order.SelectedPrototype = _random.Pick(prototypes);
                break;
            }
            case TagBatchScanCondition batch:
                order.ProgressTarget = Math.Max(1, batch.RequiredCount);
                break;
            case CanisterGasScanCondition gas:
                if (gas.AllowedGases.Count == 0)
                    return false;
                order.SelectedReagent = _random.Pick(gas.AllowedGases);
                break;
        }

        return true;
    }


    private ExperimentScannerState BuildScannerStateInternal(
        EntityUid scanner,
        ExperimentScannerDatabaseComponent scannerDb,
        ExperimentStationDatabaseComponent stationDb)
    {
        var available = stationDb.AvailableOrders.Select(ToUiData).ToList();
        var active = scannerDb.ActiveOrder == null ? null : ToUiData(scannerDb.ActiveOrder);
        var untilNextSkip = scannerDb.NextSkipTime - _timing.CurTime;

        string? serverName = null;
        var hasServer = false;
        if (TryComp<ResearchClientComponent>(scanner, out var client) && client.Server is { } server && TryComp<ResearchServerComponent>(server, out var serverComp))
        {
            hasServer = true;
            serverName = serverComp.ServerName;
        }

        return new ExperimentScannerState(available, active, untilNextSkip, hasServer, serverName);
    }

    private void UpdateAllUIs(
        EntityUid scanner,
        ExperimentStationDatabaseComponent stationDb,
        ExperimentScannerDatabaseComponent scannerDb,
        string experimentGroup,
        int visibleOrders)
    {
        FillAvailableOrders(scanner, experimentGroup, visibleOrders, stationDb);

        if (TryComp<ExperimentScannerComponent>(scanner, out var handheld))
        {
            var state = BuildScannerStateInternal(scanner, scannerDb, stationDb);
            _ui.SetUiState(scanner, ExperimentScannerUiKey.Key, state);
        }

        if (TryComp<ExperimentFloorScannerComponent>(scanner, out var floorComp))
        {
            PopulateFloorScannerState(scanner, floorComp, out var floorState);
            _ui.SetUiState(scanner, ExperimentFloorScannerUiKey.Key, floorState);
        }
    }

    private ExperimentOrderUiData ToUiData(StationExperimentOrderData order)
    {
        if (!_proto.TryIndex(order.Prototype, out var proto))
        {
            return new ExperimentOrderUiData
            {
                Id = order.Id,
                ProgressCurrent = order.ProgressCurrent,
                ProgressTarget = order.ProgressTarget
            };
        }

        var speciesName = GetSpeciesName(order.SelectedSpecies);
        var reagentName = GetReagentOrGasName(order.SelectedReagent);
        var targetName = GetEntityPrototypeName(order.SelectedPrototype);
        var desc = Loc.GetString(proto.Description,
            ("species", WrapPurple(speciesName)),
            ("reagent", WrapPurple(reagentName)),
            ("gas", WrapPurple(reagentName)),
            ("target", WrapPurple(targetName)),
            ("department", WrapPurple(order.SelectedDepartment)));

        TimeSpan? remaining = null;
        if (order.RescanAfter > _timing.CurTime)
            remaining = order.RescanAfter - _timing.CurTime;

        return new ExperimentOrderUiData
        {
            Id = order.Id,
            Name = Loc.GetString(proto.Name),
            Description = desc,
            RewardPoints = proto.RewardPoints,
            ProgressCurrent = order.ProgressCurrent,
            ProgressTarget = order.ProgressTarget,
            TimeRemaining = remaining
        };
    }

    private string? GetSpeciesName(string? speciesId)
    {
        if (speciesId == null || !_proto.TryIndex<SpeciesPrototype>(speciesId, out var species))
            return null;
        return Loc.GetString(species.Name);
    }


    private string? GetReagentOrGasName(string? reagentId)
    {
        if (reagentId == null)
            return null;
        if (_proto.TryIndex<ReagentPrototype>(reagentId, out var reagent))
            return reagent.LocalizedName;
        if (Enum.TryParse<Gas>(reagentId, true, out var gas) &&
            _proto.TryIndex<GasPrototype>(gas.ToString(), out var gasProto))
            return Loc.GetString(gasProto.Name);
        return null;
    }

    private string? GetEntityPrototypeName(string? protoId)
    {
        if (protoId == null || !_proto.TryIndex<EntityPrototype>(protoId, out var proto))
            return null;
        return Loc.GetString(proto.Name);
    }

    private static string WrapPurple(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "-" : value;
        return $"[color=#c27cff]{text}[/color]";
    }

    private void Deny(EntityUid scanner, EntityUid? user, string popupKey, SoundSpecifier? denySound = null)
    {
        if (user is not { Valid: true } validUser)
            return;

        var sound = denySound ?? new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_two.ogg");
        _audio.PlayPvs(sound, scanner);
        _popup.PopupClient(Loc.GetString(popupKey), validUser, validUser);
    }

    private bool TryGetAssignedServer(EntityUid scanner, out EntityUid server, out ResearchServerComponent serverComp)
    {
        server = default;
        serverComp = default!;
        if (!TryComp<ResearchClientComponent>(scanner, out var client) || client.Server is not { } serverUid)
            return false;
        if (!TryComp<ResearchServerComponent>(serverUid, out var foundServerComp))
            return false;
        serverComp = foundServerComp;
        server = serverUid;
        return true;
    }

    private bool TryGetAmeCoreCount(EntityUid controllerUid, out int coreCount)
    {
        coreCount = 0;
        if (!TryComp<NodeContainerComponent>(controllerUid, out var nodes))
            return false;
        var group = nodes.Nodes.Values
            .Select(node => node.NodeGroup)
            .OfType<AmeNodeGroup>()
            .FirstOrDefault();
        if (group == null)
            return false;
        coreCount = group.CoreCount;
        return true;
    }

    private bool PassesTags(EntityUid uid, List<string> required, List<string> forbidden)
    {
        foreach (var tag in required)
        {
            if (!_tag.HasTag(uid, tag))
                return false;
        }
        foreach (var tag in forbidden)
        {
            if (_tag.HasTag(uid, tag))
                return false;
        }
        return true;
    }

    private bool PassesRequiredComponents(EntityUid uid, List<string> requiredComponents)
    {
        if (requiredComponents.Count == 0)
            return false;
        foreach (var componentName in requiredComponents)
        {
            if (!EntityManager.ComponentFactory.TryGetRegistration(componentName, out var registration, true))
                return false;
            var query = EntityManager.GetEntityQuery(registration.Type);
            if (!query.HasComp(uid))
                return false;
        }
        return true;
    }

    #endregion
}

