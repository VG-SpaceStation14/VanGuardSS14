using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Ame;
using Content.Server.Ame.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Radio.EntitySystems;
using Content.Server.Research.Disk;
using Content.Server.Research.Systems;
using Content.Server.Station.Systems;
using Content.Server._VanGuard.Research.Components;
using Content.Shared._VanGuard.Research.Components;
using Content.Shared._VanGuard.Research.Prototypes;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.NodeContainer;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Research.Components;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Research.Systems;

/// <summary>
/// Coordinates station experiment orders, scanner sessions, scanning checks and rewards.
/// The handheld and floor scanners share the station order pool but keep their own active order.
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
    [Dependency] private ServerStationSystem _station = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExperimentScannerComponent, BoundUIOpenedEvent>(OnHandheldOpened);
        SubscribeLocalEvent<ExperimentScannerComponent, ExperimentSelectOrderMessage>(OnHandheldSelect);
        SubscribeLocalEvent<ExperimentScannerComponent, ExperimentAbandonOrderMessage>(OnHandheldAbandon);
        SubscribeLocalEvent<ExperimentScannerComponent, ExperimentSkipOrderMessage>(OnHandheldSkip);
        SubscribeLocalEvent<ExperimentScannerComponent, AfterInteractEvent>(OnHandheldAfterInteract);
        SubscribeLocalEvent<MetaDataComponent, InteractUsingEvent>(OnScannerUsedOnTarget);
    }

    #region Handheld events

    private void OnHandheldOpened(Entity<ExperimentScannerComponent> scanner, ref BoundUIOpenedEvent args)
    {
        if (!TryGetStationDatabase(scanner.Owner, out var station, out var stationDb))
            return;

        var scannerDb = EnsureComp<ExperimentScannerDatabaseComponent>(scanner);
        FillAvailableOrders(station, scanner.Comp.ExperimentGroup, scanner.Comp.VisibleOrders, stationDb);
        SendHandheldState(scanner, scannerDb, stationDb);
    }

    private void OnHandheldSelect(Entity<ExperimentScannerComponent> scanner, ref ExperimentSelectOrderMessage args)
    {
        SelectOrder(scanner.Owner, args.Id, args.Actor, scanner.Comp.ExperimentGroup, scanner.Comp.VisibleOrders, scanner.Comp.SelectSound);
        SendHandheldState(scanner);
    }

    private void OnHandheldAbandon(Entity<ExperimentScannerComponent> scanner, ref ExperimentAbandonOrderMessage args)
    {
        AbandonOrder(scanner.Owner, args.Actor, scanner.Comp.ExperimentGroup, scanner.Comp.VisibleOrders, scanner.Comp.SelectSound);
        SendHandheldState(scanner);
    }

    private void OnHandheldSkip(Entity<ExperimentScannerComponent> scanner, ref ExperimentSkipOrderMessage args)
    {
        SkipOrder(scanner.Owner, args.Id, args.Actor, scanner.Comp.ExperimentGroup, scanner.Comp.VisibleOrders, scanner.Comp.SkipSound);
        SendHandheldState(scanner);
    }

    private void OnHandheldAfterInteract(Entity<ExperimentScannerComponent> scanner, ref AfterInteractEvent args)
    {
        if (args.Target is { } target && args.CanReach)
            TryHandheldScan(scanner, args.User, target);
    }

    private void OnScannerUsedOnTarget(Entity<MetaDataComponent> target, ref InteractUsingEvent args)
    {
        if (!TryComp<ExperimentScannerComponent>(args.Used, out var scanner))
            return;

        TryHandheldScan((args.Used, scanner), args.User, target.Owner);
    }

    private void TryHandheldScan(Entity<ExperimentScannerComponent> scanner, EntityUid user, EntityUid target)
    {
        if (!TryComp<ExperimentScannerDatabaseComponent>(scanner, out var scannerDb) || scannerDb.ActiveOrder == null)
            return;

        if (!TryGetStationDatabase(scanner.Owner, out var station, out var stationDb))
        {
            Deny(scanner.Owner, user, "experiment-scanner-popup-no-station", scanner.Comp.DenySound);
            return;
        }

        var order = scannerDb.ActiveOrder;
        if (!TryProcessScan(station, order, target))
            return;

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):user} advanced experiment order [id:{order.Id}, prototype:{order.Prototype}, progress:{order.ProgressCurrent}/{order.ProgressTarget}] by scanning {ToPrettyString(target):entity} with scanner {ToPrettyString(scanner):entity}");

        if (order.ProgressCurrent < order.ProgressTarget)
        {
            _popup.PopupClient(
                Loc.GetString("experiment-scanner-progress-popup",
                    ("current", order.ProgressCurrent),
                    ("target", order.ProgressTarget)),
                user,
                user);
            _audio.PlayPvs(scanner.Comp.ProgressSound, scanner);
            SendHandheldState(scanner, scannerDb, stationDb);
            return;
        }

        CompleteOrder(scanner.Owner, user, station, stationDb, order, scanner.Comp.CompleteSound, scanner.Comp.AnnouncementChannel);
        FillAvailableOrders(station, scanner.Comp.ExperimentGroup, scanner.Comp.VisibleOrders, stationDb);
        SendHandheldState(scanner, scannerDb, stationDb);
    }

    private void SendHandheldState(
        Entity<ExperimentScannerComponent> scanner,
        ExperimentScannerDatabaseComponent? scannerDb = null,
        ExperimentStationDatabaseComponent? stationDb = null)
    {
        if (!TryGetStationDatabase(scanner.Owner, out _, out var resolvedStationDb))
            return;

        scannerDb ??= EnsureComp<ExperimentScannerDatabaseComponent>(scanner);
        stationDb ??= resolvedStationDb;

        var state = BuildScannerState(scanner.Owner, scannerDb, stationDb, scanner.Comp.ExperimentGroup, scanner.Comp.VisibleOrders);
        _ui.SetUiState(scanner.Owner, ExperimentScannerUiKey.Key, state);
    }

    #endregion

    #region Public scanner API

    public bool TryGetStationDatabase(
        EntityUid scanner,
        out EntityUid station,
        out ExperimentStationDatabaseComponent stationDb)
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

        if (scannerDb.LinkedStation is { } remembered && TryComp<ExperimentStationDatabaseComponent>(remembered, out var rememberedDb))
        {
            station = remembered;
            stationDb = rememberedDb;
            return true;
        }

        return false;
    }

    public void SelectOrder(
        EntityUid scanner,
        string orderId,
        EntityUid? user,
        string experimentGroup,
        int visibleOrders,
        SoundSpecifier? selectSound = null)
    {
        if (!TryComp<ExperimentScannerDatabaseComponent>(scanner, out var scannerDb))
            return;

        if (!TryGetStationDatabase(scanner, out _, out var stationDb))
        {
            Deny(scanner, user, "experiment-scanner-popup-no-station");
            return;
        }

        if (scannerDb.ActiveOrder != null)
        {
            Deny(scanner, user, "experiment-scanner-popup-already-active");
            return;
        }

        var index = stationDb.AvailableOrders.FindIndex(order => order.Id == orderId);
        if (index < 0)
        {
            Deny(scanner, user, "experiment-scanner-popup-no-available");
            return;
        }

        var accepted = stationDb.AvailableOrders[index];
        accepted.HadServerOnAccept = TryGetAssignedServer(scanner, out _, out _);
        stationDb.AvailableOrders.RemoveAt(index);
        scannerDb.ActiveOrder = accepted;

        _audio.PlayPvs(selectSound ?? new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg"), scanner);

        if (user is { Valid: true } actor)
        {
            _popup.PopupClient(Loc.GetString("experiment-scanner-popup-selected"), actor, actor);
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(actor):user} accepted experiment order [id:{accepted.Id}, prototype:{accepted.Prototype}] with scanner {ToPrettyString(scanner):entity}");
        }

        RefreshAll(scanner, stationDb, scannerDb, experimentGroup, visibleOrders);
    }

    public void AbandonOrder(
        EntityUid scanner,
        EntityUid? user,
        string experimentGroup,
        int visibleOrders,
        SoundSpecifier? selectSound = null)
    {
        if (!TryComp<ExperimentScannerDatabaseComponent>(scanner, out var scannerDb))
            return;

        if (!TryGetStationDatabase(scanner, out _, out var stationDb))
        {
            Deny(scanner, user, "experiment-scanner-popup-no-station");
            return;
        }

        if (scannerDb.ActiveOrder is not { } abandoned)
        {
            Deny(scanner, user, "experiment-scanner-popup-no-active");
            return;
        }

        stationDb.AvailableOrders.Add(abandoned);
        scannerDb.ActiveOrder = null;

        _audio.PlayPvs(selectSound ?? new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg"), scanner);

        if (user is { Valid: true } actor)
        {
            _popup.PopupClient(Loc.GetString("experiment-scanner-popup-abandoned"), actor, actor);
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(actor):user} abandoned experiment order [id:{abandoned.Id}, prototype:{abandoned.Prototype}] with scanner {ToPrettyString(scanner):entity}");
        }

        RefreshAll(scanner, stationDb, scannerDb, experimentGroup, visibleOrders);
    }

    public void SkipOrder(
        EntityUid scanner,
        string orderId,
        EntityUid? user,
        string experimentGroup,
        int visibleOrders,
        SoundSpecifier? skipSound = null)
    {
        if (!TryComp<ExperimentScannerDatabaseComponent>(scanner, out var scannerDb))
            return;

        if (!TryGetStationDatabase(scanner, out var station, out var stationDb))
        {
            Deny(scanner, user, "experiment-scanner-popup-no-station");
            return;
        }

        if (_timing.CurTime < scannerDb.NextSkipTime)
        {
            Deny(scanner, user, "experiment-scanner-popup-skip-cooldown");
            return;
        }

        var index = stationDb.AvailableOrders.FindIndex(order => order.Id == orderId);
        if (index < 0)
        {
            Deny(scanner, user, "experiment-scanner-popup-no-available");
            return;
        }

        var discarded = stationDb.AvailableOrders[index];
        stationDb.AvailableOrders.RemoveAt(index);

        if (!TryAddOrder(station, experimentGroup, stationDb, discarded.Prototype))
        {
            stationDb.AvailableOrders.Insert(index, discarded);
            Deny(scanner, user, "experiment-scanner-popup-no-available");
            return;
        }

        scannerDb.NextSkipTime = _timing.CurTime + scannerDb.SkipDelay;
        _audio.PlayPvs(skipSound ?? new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg"), scanner);

        if (user is { Valid: true } actor)
        {
            _popup.PopupClient(Loc.GetString("experiment-scanner-popup-skipped"), actor, actor);
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(actor):user} skipped experiment order [id:{discarded.Id}, prototype:{discarded.Prototype}] with scanner {ToPrettyString(scanner):entity}");
        }

        RefreshAll(scanner, stationDb, scannerDb, experimentGroup, visibleOrders);
    }

    public bool TryProcessScan(EntityUid station, StationExperimentOrderData order, EntityUid target)
    {
        return AdvanceOrder(station, order, target);
    }

    public void CompleteOrder(
        EntityUid scanner,
        EntityUid? user,
        EntityUid station,
        ExperimentStationDatabaseComponent stationDb,
        StationExperimentOrderData order,
        SoundSpecifier? completeSound = null,
        ProtoId<RadioChannelPrototype>? channel = null)
    {
        var scannerDb = EnsureComp<ExperimentScannerDatabaseComponent>(scanner);
        if (scannerDb.ActiveOrder == null || scannerDb.ActiveOrder.Id != order.Id)
            return;

        var proto = _proto.Index(order.Prototype);
        var rewardDelivered = false;

        if (TryGetAssignedServer(scanner, out var server, out var serverComp))
        {
            _research.ModifyServerPoints(server, proto.RewardPoints, serverComp);
            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(user):user} completed experiment order [id:{order.Id}, prototype:{order.Prototype}] and awarded {proto.RewardPoints} points to research server {ToPrettyString(server):entity} using scanner {ToPrettyString(scanner):entity}");
            rewardDelivered = true;
        }
        else if (!order.HadServerOnAccept)
        {
            var disk = Spawn("ResearchDisk", Transform(scanner).Coordinates);
            if (TryComp<ResearchDiskComponent>(disk, out var diskComp))
                diskComp.Points = proto.RewardPoints;

            if (user is { Valid: true } actor)
                _popup.PopupClient(Loc.GetString("experiment-scanner-disk-fallback-popup", ("points", proto.RewardPoints)), actor, actor);

            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(user):user} completed experiment order [id:{order.Id}, prototype:{order.Prototype}] without server link; spawned fallback research disk {ToPrettyString(disk):entity} with {proto.RewardPoints} points using scanner {ToPrettyString(scanner):entity}");
            rewardDelivered = true;
        }

        if (!rewardDelivered)
        {
            if (user is { Valid: true } actor)
                _popup.PopupClient(Loc.GetString("experiment-scanner-popup-no-server"), actor, actor);
            return;
        }

        if (user is { Valid: true } completedUser)
            _popup.PopupClient(Loc.GetString("experiment-scanner-complete-popup"), completedUser, completedUser);

        var identity = new TryGetIdentityShortInfoEvent(scanner, user.GetValueOrDefault(), true);
        RaiseLocalEvent(identity);
        var performer = identity.Title ?? Loc.GetString("experiment-scanner-complete-radio-unknown");
        var announcement = Loc.GetString("experiment-scanner-complete-radio-broadcast",
            ("order", Loc.GetString(proto.Name)),
            ("performer", performer),
            ("points", proto.RewardPoints));

        _radio.SendRadioMessage(scanner, announcement, channel ?? new ProtoId<RadioChannelPrototype>("Science"), scanner, escapeMarkup: false);
        _audio.PlayPvs(completeSound ?? new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg"), scanner);

        stationDb.UsedOrders.Add(order.Prototype);
        scannerDb.ActiveOrder = null;
    }

    public void FillAvailableOrders(
        EntityUid station,
        string experimentGroup,
        int visibleOrders,
        ExperimentStationDatabaseComponent db,
        Func<ResearchExperimentPrototype, bool>? filter = null)
    {
        while (db.AvailableOrders.Count < visibleOrders)
        {
            if (!TryAddOrder(station, experimentGroup, db, filter: filter))
                break;
        }
    }

    public ExperimentScannerState BuildScannerState(
        EntityUid scanner,
        ExperimentScannerDatabaseComponent scannerDb,
        ExperimentStationDatabaseComponent? stationDb,
        string experimentGroup,
        int visibleOrders)
    {
        if (stationDb == null || !TryGetStationDatabase(scanner, out var station, out _))
            return new ExperimentScannerState(new List<ExperimentOrderUiData>(), null, TimeSpan.Zero, false, null);

        FillAvailableOrders(station, experimentGroup, visibleOrders, stationDb);
        return BuildHandheldState(scanner, scannerDb, stationDb);
    }

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

    public bool IsFloorCompatibleOrder(string orderId, EntityUid scanner)
    {
        if (!TryGetStationDatabase(scanner, out _, out var stationDb))
            return false;

        var order = stationDb.AvailableOrders.FirstOrDefault(candidate => candidate.Id == orderId);
        return order != null
               && _proto.TryIndex(order.Prototype, out var proto)
               && IsFloorCompatible(proto);
    }

    public void FillFloorAvailableOrders(EntityUid station, string experimentGroup, int visibleOrders, ExperimentStationDatabaseComponent db)
    {
        var compatible = db.AvailableOrders.Count(order => _proto.TryIndex(order.Prototype, out var proto) && IsFloorCompatible(proto));
        while (compatible < visibleOrders)
        {
            if (!TryAddOrder(station, experimentGroup, db, filter: IsFloorCompatible))
                break;
            compatible++;
        }
    }

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
            .Where(order => _proto.TryIndex(order.Prototype, out var proto) && IsFloorCompatible(proto))
            .Select(ToUiData)
            .ToList();

        var active = scannerDb.ActiveOrder == null ? null : ToUiData(scannerDb.ActiveOrder);
        var untilNextSkip = scannerDb.NextSkipTime - _timing.CurTime;
        var (hasServer, serverName) = GetServerInfo(scanner);

        state = new ExperimentFloorScannerState(
            available,
            active,
            untilNextSkip,
            hasServer,
            serverName,
            floorComp.IsProcessing);
    }

    #endregion

    #region UI and rewards

    private ExperimentScannerState BuildHandheldState(
        EntityUid scanner,
        ExperimentScannerDatabaseComponent scannerDb,
        ExperimentStationDatabaseComponent stationDb)
    {
        var available = stationDb.AvailableOrders.Select(ToUiData).ToList();
        var active = scannerDb.ActiveOrder == null ? null : ToUiData(scannerDb.ActiveOrder);
        var untilNextSkip = scannerDb.NextSkipTime - _timing.CurTime;
        var (hasServer, serverName) = GetServerInfo(scanner);

        return new ExperimentScannerState(available, active, untilNextSkip, hasServer, serverName);
    }

    private void RefreshAll(
        EntityUid scanner,
        ExperimentStationDatabaseComponent stationDb,
        ExperimentScannerDatabaseComponent scannerDb,
        string experimentGroup,
        int visibleOrders)
    {
        if (!TryGetStationDatabase(scanner, out var station, out _))
            return;

        FillAvailableOrders(station, experimentGroup, visibleOrders, stationDb);

        if (TryComp<ExperimentScannerComponent>(scanner, out var handheld))
        {
            var state = BuildHandheldState(scanner, scannerDb, stationDb);
            _ui.SetUiState(scanner, ExperimentScannerUiKey.Key, state);
        }

        if (TryComp<ExperimentFloorScannerComponent>(scanner, out var floor))
        {
            PopulateFloorScannerState(scanner, floor, out var floorState);
            _ui.SetUiState(scanner, ExperimentFloorScannerUiKey.Key, floorState);
        }
    }

    private (bool HasServer, string? ServerName) GetServerInfo(EntityUid scanner)
    {
        if (TryComp<ResearchClientComponent>(scanner, out var client)
            && client.Server is { } server
            && TryComp<ResearchServerComponent>(server, out var serverComp))
            return (true, serverComp.ServerName);

        return (false, null);
    }

    private ExperimentOrderUiData ToUiData(StationExperimentOrderData order)
    {
        if (!_proto.TryIndex(order.Prototype, out var proto))
        {
            return new ExperimentOrderUiData
            {
                Id = order.Id,
                ProgressCurrent = order.ProgressCurrent,
                ProgressTarget = order.ProgressTarget,
            };
        }

        var species = GetSpeciesName(order.SelectedSpecies);
        var reagent = GetReagentOrGasName(order.SelectedReagent);
        var target = GetPrototypeName(order.SelectedPrototype);
        var description = Loc.GetString(proto.Description,
            ("species", Colorize(species)),
            ("reagent", Colorize(reagent)),
            ("gas", Colorize(reagent)),
            ("target", Colorize(target)),
            ("department", Colorize(order.SelectedDepartment)));

        TimeSpan? remaining = null;
        if (order.RescanAfter > _timing.CurTime)
            remaining = order.RescanAfter - _timing.CurTime;

        return new ExperimentOrderUiData
        {
            Id = order.Id,
            Name = Loc.GetString(proto.Name),
            Description = description,
            RewardPoints = proto.RewardPoints,
            ProgressCurrent = order.ProgressCurrent,
            ProgressTarget = order.ProgressTarget,
            TimeRemaining = remaining,
        };
    }

    private string? GetSpeciesName(string? speciesId)
    {
        if (speciesId == null || !_proto.TryIndex<SpeciesPrototype>(speciesId, out var species))
            return null;

        return Loc.GetString(species.Name);
    }

    private string? GetReagentOrGasName(string? id)
    {
        if (id == null)
            return null;

        if (_proto.TryIndex<ReagentPrototype>(id, out var reagent))
            return reagent.LocalizedName;

        if (Enum.TryParse<Gas>(id, true, out var gas)
            && _proto.TryIndex<GasPrototype>(gas.ToString(), out var gasProto))
            return Loc.GetString(gasProto.Name);

        return null;
    }

    private string? GetPrototypeName(string? id)
    {
        if (id == null || !_proto.TryIndex<EntityPrototype>(id, out var proto))
            return null;

        return Loc.GetString(proto.Name);
    }

    private static string Colorize(string? value)
    {
        return $"[color=#c27cff]{(string.IsNullOrWhiteSpace(value) ? "-" : value)}[/color]";
    }

    private void Deny(EntityUid scanner, EntityUid? user, string popupKey, SoundSpecifier? denySound = null)
    {
        if (user is not { Valid: true } actor)
            return;

        _audio.PlayPvs(denySound ?? new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_two.ogg"), scanner);
        _popup.PopupClient(Loc.GetString(popupKey), actor, actor);
    }

    private bool TryGetAssignedServer(EntityUid scanner, out EntityUid server, out ResearchServerComponent serverComp)
    {
        server = default;
        serverComp = default!;

        if (!TryComp<ResearchClientComponent>(scanner, out var client)
            || client.Server is not { } serverUid
            || !TryComp<ResearchServerComponent>(serverUid, out var found))
            return false;

        server = serverUid;
        serverComp = found;
        return true;
    }

    #endregion
}
