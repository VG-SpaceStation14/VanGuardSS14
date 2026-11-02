using System.Linq;
using Content.Server._VanGuard.Lavaland;
using Content.Server._VanGuard.Shuttles.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._VanGuard.Shuttles;
using Content.Shared.Access.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Station.Components;
using Content.Shared.Station.Systems;
using Content.Shared.Tag;
using Content.Shared.Timing;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Shuttles.Systems;

/// <summary>
/// Server half of the mining shuttle console. The console exposes one fixed
/// direction for the owning station and one for the Lavaland outpost, then
/// delegates the movement to the stock shuttle/docking FTL systems.
/// </summary>
public sealed partial class MiningShuttleConsoleSystem : EntitySystem
{
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private StationSystem _station = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MiningShuttleComponent, ComponentStartup>(OnShuttleStartup);
        SubscribeLocalEvent<MiningShuttleConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<DockEvent>(OnDock);
        SubscribeLocalEvent<UndockEvent>(OnUndock);
        SubscribeLocalEvent<FTLStartedEvent>(OnFtlStarted);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFtlCompleted);

        Subs.BuiEvents<MiningShuttleConsoleComponent>(
            MiningShuttleConsoleUiKey.MiningShuttleConsole,
            subs =>
            {
                subs.Event<MiningShuttleConsoleTravelMessage>(OnTravel);
            });
    }

    private void OnShuttleStartup(Entity<MiningShuttleComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.Station ??= _station.GetOwningStation(ent.Owner);
    }

    private void OnOpened(Entity<MiningShuttleConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not MiningShuttleConsoleUiKey.MiningShuttleConsole)
            return;

        UpdateUi(ent);
    }

    private void OnTravel(Entity<MiningShuttleConsoleComponent> ent, ref MiningShuttleConsoleTravelMessage args)
    {
        TryTravel(ent, args.Index, args.Actor, out var error);
        UpdateUi(ent, error);
    }

    private void OnDock(DockEvent args)
    {
        UpdateConsolesUsing(args.GridAUid);
        UpdateConsolesUsing(args.GridBUid);
    }

    private void OnUndock(UndockEvent args)
    {
        UpdateConsolesUsing(args.GridAUid);
        UpdateConsolesUsing(args.GridBUid);
    }

    private TimeSpan _refreshTimer;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _refreshTimer += TimeSpan.FromSeconds(frameTime);
        if (_refreshTimer < TimeSpan.FromSeconds(1))
            return;

        _refreshTimer = TimeSpan.Zero;
        var consoles = AllEntityQuery<MiningShuttleConsoleComponent>();
        while (consoles.MoveNext(out var uid, out var component))
            UpdateUi((uid, component));
    }

    private void OnFtlStarted(ref FTLStartedEvent args)
    {
        UpdateConsolesUsing(args.Entity);
    }

    private void OnFtlCompleted(ref FTLCompletedEvent args)
    {
        UpdateConsolesUsing(args.Entity);
    }

    /// <summary>
    /// Returns the shuttle controlled by this console. An explicit link is
    /// validated first; otherwise the marked mining shuttle is used.
    /// </summary>
    public bool TryGetShuttle(Entity<MiningShuttleConsoleComponent> console, out EntityUid shuttle)
    {
        shuttle = default;

        if (console.Comp.LinkedShuttle is { } linked)
        {
            if (!Exists(linked) || !HasComp<ShuttleComponent>(linked))
            {
                Log.Warning($"Mining console {ToPrettyString(console.Owner)} has an invalid linked shuttle {linked}.");
                return false;
            }

            shuttle = linked;
            return true;
        }

        var currentGrid = Transform(console.Owner).GridUid;
        if (currentGrid is { } current &&
            HasComp<ShuttleComponent>(current) &&
            HasComp<MiningShuttleComponent>(current))
        {
            shuttle = current;
            return true;
        }

        EntityUid? found = null;
        var query = AllEntityQuery<MiningShuttleComponent>();
        while (query.MoveNext(out var candidate, out var marker))
        {
            if (!HasComp<ShuttleComponent>(candidate) || marker.DockTag != console.Comp.DockTag)
                continue;

            if (found != null)
            {
                Log.Warning(
                    $"Mining console {ToPrettyString(console.Owner)} found multiple marked shuttles; " +
                    "set LinkedShuttle explicitly.");
                return false;
            }

            found = candidate;
        }

        if (found is { } result)
        {
            shuttle = result;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The two fixed directions shown by the console. The order is stable:
    /// station first, Lavaland second.
    /// </summary>
    public List<MiningShuttleDestination> GetDestinations(
        Entity<MiningShuttleConsoleComponent> console,
        EntityUid shuttle)
    {
        var result = new List<MiningShuttleDestination>
        {
            new(Loc.GetString("mining-console-station"), false, Loc.GetString("mining-console-no-station")),
            new(Loc.GetString("mining-console-lavaland"), false, Loc.GetString("mining-console-no-lavaland")),
        };

        var canFtl = _shuttle.CanFTL(shuttle, out var ftlReason);
        var stationMap = GetStationMap(shuttle);
        if (stationMap is { } stationMapId &&
            FindDockTarget(stationMapId, console.Comp.DockTag, shuttle) is { } stationGrid)
        {
            var enabled = canFtl && _shuttle.CanFTLTo(shuttle, stationMapId, console.Owner);
            result[0] = new MiningShuttleDestination(
                result[0].Name,
                enabled,
                !canFtl
                    ? ftlReason
                    : enabled
                        ? null
                        : Loc.GetString("mining-console-destination-unavailable"));
        }

        if (TryGetLavaland(out var lavalandMap, out var lavalandGrid))
        {
            var enabled = canFtl && _shuttle.CanFTLTo(shuttle, lavalandMap, console.Owner);
            if (lavalandGrid != EntityUid.Invalid)
            {
                result[1] = new MiningShuttleDestination(
                    result[1].Name,
                    enabled,
                    !canFtl
                        ? ftlReason
                        : enabled
                            ? null
                            : Loc.GetString("mining-console-destination-unavailable"));
            }
        }

        return result;
    }

    /// <summary>
    /// Sends the shuttle to one of the two fixed directions. The index comes
    /// from the client, so the destination is resolved again on the server.
    /// </summary>
    public bool TryTravel(
        Entity<MiningShuttleConsoleComponent> console,
        int index,
        EntityUid actor,
        out string? error)
    {
        error = null;

        if (!_access.IsAllowed(actor, console.Owner))
        {
            error = Loc.GetString("mining-console-access-denied");
            return false;
        }

        if (!TryGetShuttle(console, out var shuttle) ||
            !TryComp<ShuttleComponent>(shuttle, out var shuttleComp))
        {
            error = Loc.GetString("mining-console-no-shuttle");
            return false;
        }

        if (!_shuttle.CanFTL(shuttle, out var ftlReason))
        {
            error = ftlReason;
            return false;
        }

        if (!TryResolveDestination(console, shuttle, index, out var targetMap, out var targetGrid))
        {
            error = Loc.GetString(index == 0
                ? "mining-console-no-station"
                : "mining-console-no-lavaland");
            return false;
        }

        if (!_shuttle.CanFTLTo(shuttle, targetMap, console.Owner))
        {
            error = Loc.GetString("mining-console-destination-unavailable");
            return false;
        }

        _shuttle.FTLToDock(
            shuttle,
            shuttleComp,
            targetGrid,
            priorityTag: console.Comp.DockTag);

        // FTLToDock is void. Do not report success unless the stock system
        // actually entered FTL; this prevents a silent lost-shuttle state.
        if (!HasComp<FTLComponent>(shuttle))
        {
            error = Loc.GetString("mining-console-ftl-failed");
            return false;
        }

        return true;
    }

    private bool TryResolveDestination(
        Entity<MiningShuttleConsoleComponent> console,
        EntityUid shuttle,
        int index,
        out MapId map,
        out EntityUid grid)
    {
        map = default;
        grid = default;

        if (index == 0)
        {
            if (GetStationMap(shuttle) is not { } stationMap)
                return false;

            map = stationMap;
            if (FindDockTarget(stationMap, console.Comp.DockTag, shuttle) is not { } stationGrid)
                return false;

            grid = stationGrid;
            return true;
        }

        if (index != 1 || !TryGetLavaland(out var lavalandMap, out var lavalandGrid))
            return false;

        map = lavalandMap;
        grid = lavalandGrid;
        return true;
    }

    private MapId? GetStationMap(EntityUid shuttle)
    {
        if (!TryComp<MiningShuttleComponent>(shuttle, out var marker))
            return null;

        if (marker.Station == null || !Exists(marker.Station.Value))
        {
            marker.Station = _station.GetOwningStation(shuttle);
        }

        if (marker.Station is not { } station || !TryComp<StationDataComponent>(station, out var data))
            return null;

        MapId? map = null;
        var largest = 0f;

        foreach (var gridUid in data.Grids)
        {
            if (!TryComp<MapGridComponent>(gridUid, out var grid))
                continue;

            var area = grid.LocalAABB.Size.LengthSquared();
            if (area < largest)
                continue;

            largest = area;
            map = Transform(gridUid).MapID;
        }

        return map;
    }

    private bool TryGetLavaland(out MapId map, out EntityUid outpostGrid)
    {
        var query = AllEntityQuery<LavalandOutpostRuleComponent>();
        while (query.MoveNext(out _, out var rule))
        {
            if (rule.MapId is not { } mapId || rule.OutpostGrid is not { } grid)
                continue;

            map = mapId;
            outpostGrid = grid;
            return true;
        }

        map = default;
        outpostGrid = EntityUid.Invalid;
        return false;
    }

    /// <summary>
    /// Mirrors the stock console behaviour: prefer a tagged dock on the map,
    /// otherwise use the largest non-shuttle grid on that map.
    /// </summary>
    private EntityUid? FindDockTarget(MapId map, ProtoId<TagPrototype> tag, EntityUid shuttle)
    {
        var query = AllEntityQuery<PriorityDockComponent, TransformComponent>();
        while (query.MoveNext(out _, out var priority, out var xform))
        {
            if (priority.Tag != tag ||
                xform.MapID != map ||
                xform.GridUid is not { } taggedGrid ||
                taggedGrid == shuttle ||
                !_docking.GetDocks(taggedGrid).Any(dock => !dock.Comp.Docked))
            {
                continue;
            }

            return taggedGrid;
        }

        return FindLargestGrid(map, shuttle);
    }

    private EntityUid? FindLargestGrid(MapId map, EntityUid shuttle)
    {
        EntityUid? largestGrid = null;
        var largestSize = 0f;

        var stations = AllEntityQuery<StationDataComponent>();
        while (stations.MoveNext(out _, out var data))
        {
            foreach (var gridUid in data.Grids)
            {
                if (gridUid == shuttle ||
                    Transform(gridUid).MapID != map ||
                    !TryComp<MapGridComponent>(gridUid, out var stationGrid) ||
                    !_docking.GetDocks(gridUid).Any(dock => !dock.Comp.Docked))
                {
                    continue;
                }

                var size = stationGrid.LocalAABB.Size.LengthSquared();
                if (size < largestSize)
                    continue;

                largestSize = size;
                largestGrid = gridUid;
            }
        }

        if (largestGrid != null)
            return largestGrid;

        var grids = AllEntityQuery<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out var gridUid, out var grid, out var xform))
        {
            if (gridUid == shuttle ||
                xform.MapID != map ||
                HasComp<ShuttleComponent>(gridUid) ||
                !_docking.GetDocks(gridUid).Any(dock => !dock.Comp.Docked))
            {
                continue;
            }

            var size = grid.LocalAABB.Size.LengthSquared();
            if (size < largestSize)
                continue;

            largestSize = size;
            largestGrid = gridUid;
        }

        return largestGrid;
    }

    private void UpdateConsolesUsing(EntityUid shuttle)
    {
        var consoles = AllEntityQuery<MiningShuttleConsoleComponent>();
        while (consoles.MoveNext(out var uid, out var component))
        {
            var console = (uid, component);
            if (TryGetShuttle(console, out var controlled) && controlled == shuttle)
                UpdateUi(console);
        }
    }

    private void UpdateUi(Entity<MiningShuttleConsoleComponent> console, string? error = null)
    {
        if (!TryGetShuttle(console, out var shuttle))
        {
            _ui.SetUiState(
                console.Owner,
                MiningShuttleConsoleUiKey.MiningShuttleConsole,
                new MiningShuttleConsoleState(
                    Loc.GetString("mining-console-title"),
                    FTLState.Available,
                    default,
                    false,
                    new List<MiningShuttleDestination>(),
                    error ?? Loc.GetString("mining-console-no-shuttle")));
            return;
        }

        var ftlState = FTLState.Available;
        StartEndTime ftlTime = default;
        if (TryComp<FTLComponent>(shuttle, out var ftl))
        {
            ftlState = ftl.State;
            ftlTime = _shuttle.GetStateTime(ftl);
        }

        var destinations = GetDestinations(console, shuttle);
        _ui.SetUiState(
            console.Owner,
            MiningShuttleConsoleUiKey.MiningShuttleConsole,
            new MiningShuttleConsoleState(
                Loc.GetString("mining-console-title"),
                ftlState,
                ftlTime,
                true,
                destinations,
                error));
    }
}
