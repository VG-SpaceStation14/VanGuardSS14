using System.Linq;
using Content.Server._VanGuard.Lavaland;
using Content.Server._VanGuard.Shuttles.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Content.Shared.GameTicking.Components;
using Content.Shared.GameTicking.Rules;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Content.Shared.Tag;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._VanGuard.Shuttles.Systems;

/// <summary>
/// Spawns the mining shuttle at round start and docks it to the first free station
/// dock with the mining tag. If the station has no suitable dock, the shuttle is
/// docked to the Lavaland outpost instead and a warning is written to the log.
/// </summary>
public sealed partial class MiningShuttleSpawnRuleSystem : GameRuleSystem<MiningShuttleSpawnRuleComponent>
{
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private ServerStationSystem _station = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private MetaDataSystem _meta = default!;

    public override void Initialize()
    {
        base.Initialize();

        // RoundstartStationVariation adds this rule before the station's
        // StationPostInitEvent, so wait for the station grids before spawning.
        SubscribeLocalEvent<StationPostInitEvent>(OnStationPostInit, after: new[] { typeof(ShuttleSystem) });
    }

    protected override void Added(
        Entity<MiningShuttleSpawnRuleComponent, GameRuleComponent> rule,
        ref GameRuleAddedEvent args)
    {
        base.Added(rule, ref args);

        if (rule.Comp1.ShuttleGrid is { } existing && Exists(existing))
            return;

        // No station yet: leave the rule pending and retry from StationPostInitEvent.
        if (!TryFindStationGrid(out _))
            return;

        SpawnShuttle(rule);
    }

    private void OnStationPostInit(ref StationPostInitEvent args)
    {
        foreach (var rule in GameTicker.GetAddedGameRules<MiningShuttleSpawnRuleComponent>())
        {
            if (rule.Comp.ShuttleGrid is { } existing && Exists(existing))
                continue;

            if (TryComp<GameRuleComponent>(rule.Owner, out var gameRule))
                SpawnShuttle((rule.Owner, rule.Comp, gameRule));
        }
    }

    private void SpawnShuttle(Entity<MiningShuttleSpawnRuleComponent, GameRuleComponent> rule)
    {
        var component = rule.Comp1;

        if (!TryFindStationGrid(out var stationGrid))
        {
            Log.Error("Mining shuttle could not be spawned: no station grid was found.");
            return;
        }

        var stationMap = Transform(stationGrid).MapID;
        if (!_loader.TryLoadGrid(
                stationMap,
                component.ShuttlePath,
                out var shuttle,
                new DeserializationOptions { InitializeMaps = true }))
        {
            Log.Error($"Mining shuttle could not be loaded from {component.ShuttlePath}.");
            return;
        }

        component.ShuttleGrid = shuttle.Value;
        _meta.SetEntityName(shuttle.Value, Loc.GetString(component.ShuttleName));

        if (TryDockToStation(shuttle.Value, stationMap, component.DockTag))
            return;

        if (TryDockToFallback(shuttle.Value, component.DockTag))
        {
            component.DockedToFallback = true;
            Log.Warning(
                "Mining shuttle found no free tagged dock on the station; docked to the Lavaland outpost instead.");
            return;
        }

        Log.Warning(
            "Mining shuttle found no free tagged dock on the station or the Lavaland outpost; " +
            "the shuttle remains loaded at its station-map origin.");
    }

    private bool TryFindStationGrid(out EntityUid stationGrid)
    {
        stationGrid = default;

        var stations = AllEntityQuery<StationDataComponent>();
        while (stations.MoveNext(out var station, out var data))
        {
            if (_station.GetLargestGrid((station, data)) is not { } grid)
                continue;

            stationGrid = grid;
            return true;
        }

        return false;
    }

    private bool TryDockToStation(EntityUid shuttle, MapId stationMap, ProtoId<TagPrototype> tag)
    {
        var docks = AllEntityQuery<PriorityDockComponent, DockingComponent, TransformComponent>();
        while (docks.MoveNext(out var dock, out var priority, out var docking, out var xform))
        {
            if (priority.Tag != tag || docking.Docked)
                continue;

            if (xform.GridUid is not { } targetGrid ||
                Transform(targetGrid).MapID != stationMap ||
                targetGrid == shuttle)
            {
                continue;
            }

            if (TryDock(shuttle, targetGrid, tag))
                return true;
        }

        return false;
    }

    private bool TryDockToFallback(EntityUid shuttle, ProtoId<TagPrototype> tag)
    {
        foreach (var rule in GameTicker.GetAddedGameRules<LavalandOutpostRuleComponent>())
        {
            if (rule.Comp.OutpostGrid is not { } outpost || !Exists(outpost))
                continue;

            if (TryDock(shuttle, outpost, tag))
                return true;
        }

        return false;
    }

    private bool TryDock(EntityUid shuttle, EntityUid targetGrid, ProtoId<TagPrototype> tag)
    {
        if (!TryComp<ShuttleComponent>(shuttle, out var shuttleComp))
            return false;

        if (_docking.GetDockingConfig(shuttle, targetGrid, tag) == null)
            return false;

        return _shuttle.TryFTLDock(shuttle, shuttleComp, targetGrid, tag);
    }
}
