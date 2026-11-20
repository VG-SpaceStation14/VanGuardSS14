using System.Numerics;
using Content.Server._VanGuard.Lavaland.Systems;
using Content.Server._VanGuard.Lavaland.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server._VanGuard.Shuttles.Components;
using Content.Server.Maps;
using Content.Shared._VanGuard.CCVars;
using Content.Server.Parallax;
using Content.Shared.GameTicking;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Salvage;
using Content.Server._VanGuard.Weather;
using Content.Server.Salvage;
using Content.Shared.Shuttles.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Applies the Lavaland planet setup after a map has been loaded by a <c>LoadMapRule</c>.
/// </summary>
public sealed partial class LavalandOutpostRuleSystem : EntitySystem
{
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private LavalandPoiSpawnerSystem _poi = default!;
    [Dependency] private RestrictedRangeSystem _restricted = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IGameMapManager _gameMapManager = default!;
    [Dependency] private IConfigurationManager _configuration = default!;

    private bool _automaticLavalandLoaded;
    private MapId? _automaticLavalandMap;

    /// <summary>
    /// Local AABB of the Lavaland mining outpost grid, measured once at startup
    /// by loading it into a throwaway map. The map is then deleted so the
    /// outpost never actually exists in the world before its real placement.
    /// </summary>
    private Box2? _outpostLocalAabb;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LavalandOutpostRuleComponent, RuleLoadedGridsEvent>(OnRuleLoadedGrids);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _automaticLavalandLoaded = false);
        SubscribeLocalEvent<LavalandOutpostSpawnerFinishedEvent>(OnOutpostSpawnerFinished);
    }

    private void OnRuleLoadedGrids(Entity<LavalandOutpostRuleComponent> ent, ref RuleLoadedGridsEvent args)
    {
        var mapUid = _map.GetMapOrInvalid(args.Map);
        if (!Exists(mapUid))
        {
            Log.Error($"Lavaland rule loaded an invalid map {args.Map}.");
            return;
        }

        ent.Comp.Map = mapUid;
        ent.Comp.MapId = args.Map;

        // The automatic loader is attached to the station variation, which can be
        // instantiated once per station. Lavaland only exists once per round.
        if (ent.Comp.SkipOnEmptyMap)
        {
            if (!_configuration.GetCVar(VGCCVars.LavalandRoundstart))
                return;

            if (_automaticLavalandLoaded)
                return;

            // A test/empty map has no station gameplay and must not pay for a full
            // Lavaland bootstrap. Explicit LavalandLoader rules keep SkipOnEmptyMap disabled.
            if (_gameMapManager.GetSelectedMap()?.ID == "Empty")
            {
                Log.Info("Skipping Lavaland bootstrap on the empty map.");
                return;
            }

            _automaticLavalandLoaded = true;

            // LoadMapRule creates a new map for every automatic round-start
            // instance. Remove the previous round's planet so it cannot accumulate
            // in reused test pairs (and in long-running servers).
            if (_automaticLavalandMap is { } previous && _map.MapExists(previous))
                _map.QueueDeleteMap(previous);

            _automaticLavalandMap = args.Map;
        }

        _map.SetPaused(mapUid, false);
        _meta.SetEntityName(mapUid, Loc.GetString("lavaland-map-name"));

        // EnsurePlanet adds MapGrid, Biome, Gravity, MapLight, Roof, LightCycle,
        // SunShadow and SunShadowCycle, and sets a standard 20 C atmosphere.
        _biome.EnsurePlanet(
            mapUid,
            ProtoMan.Index(ent.Comp.Biome),
            ent.Comp.Seed,
            mapLight: ent.Comp.MapLight);

        ApplyPlanetOverrides(mapUid, ent.Comp);
        EnsureComp<LavalandWeatherSchedulerComponent>(mapUid);
        ApplyMarkerLayers(mapUid, ent.Comp);

        // Phase A — reserve the outpost's future footprint *without* loading
        // the outpost grid itself. We need its LocalAABB to compute world
        // bounds; the grid is measured once on first use by loading it into a
        // throwaway map that is deleted immediately afterwards.
        var localAabb = EnsureOutpostLocalAabb(ent);
        var origin = Vector2.Zero;
        var outpostBounds = localAabb.Translated(origin).Enlarged(ent.Comp.ClearRadius);
        _biome.ReserveTiles(mapUid, outpostBounds, new List<(Vector2i, Tile)>());

        // Phase B — run the existing POI generator. POI candidates will see
        // the reserved outpost bounds in the occupancy list and steer away
        // from them. The outpost grid does not yet exist, so no transient
        // dungeon grid can ever overlap real outpost entities.
        if (TryComp<LavalandPoiSpawnerComponent>(ent.Owner, out var poi))
            _poi.SpawnPois((ent.Owner, poi), mapUid, origin, outpostBounds);

        // Phase C — defer outpost loading until the next tick. By then
        // EntityManager.ProcessQueueudDeletions has already removed every
        // dungeon grid that the spawner rejected, and PhysicsSystem.TickUpdate
        // has already run for the current tick. The outpost will then be the
        // only grid in its area, so there is no possibility of physical
        // contact between it and a discarded dungeon grid.
        EntityManager.EventBus.QueueEvent(EventSource.Local, new LavalandOutpostSpawnerFinishedEvent(
            mapUid,
            args.Map,
            origin,
            outpostBounds));

        var destination = EnsureComp<FTLDestinationComponent>(mapUid);
        destination.Enabled = true;
        destination.BeaconsOnly = false;
        destination.RequireCoordinateDisk = false;
        destination.Whitelist = null;
        Dirty(mapUid, destination);

        var restriction = EnsureComp<RestrictedRangeComponent>(mapUid);
        restriction.Range = ent.Comp.MapBorderRadius;
        restriction.BoundaryEntity = _restricted.CreateBoundary(new EntityCoordinates(mapUid, 0, 0), ent.Comp.MapBorderRadius);
    }

    private Box2 EnsureOutpostLocalAabb(Entity<LavalandOutpostRuleComponent> ent)
    {
        if (_outpostLocalAabb is { } cached)
            return cached;

        // Measure the outpost's LocalAABB by loading it into a throwaway map
        // that is deleted before the function returns. The grid never exists
        // in any visible map.
        var measureMapUid = _map.CreateMap(out _);
        try
        {
            if (!_loader.TryLoadGrid(
                    Transform(measureMapUid).MapID,
                    ent.Comp.GridPath,
                    out var loaded,
                    new DeserializationOptions { InitializeMaps = false }))
            {
                Log.Error($"Failed to measure Lavaland outpost grid {ent.Comp.GridPath}.");
                return Box2.Empty;
            }

            if (loaded is not { } outpost ||
                !TryComp<MapGridComponent>(outpost.Owner, out var grid))
            {
                Log.Error("Lavaland outpost measurement grid loaded without a MapGridComponent.");
                return Box2.Empty;
            }

            _outpostLocalAabb = grid.LocalAABB;
            return grid.LocalAABB;
        }
        finally
        {
            _map.QueueDeleteMap(Transform(measureMapUid).MapID);
        }
    }

    private void OnOutpostSpawnerFinished(LavalandOutpostSpawnerFinishedEvent ev)
    {
        var query = EntityQueryEnumerator<LavalandOutpostRuleComponent>();
        while (query.MoveNext(out var ruleEnt, out var rule))
        {
            if (rule.Map != ev.MapUid)
                continue;

            LoadOutpostGrid((ruleEnt, rule), ev.MapUid, ev.Origin, ev.OutpostBounds);
        }
    }

    private void LoadOutpostGrid(
        Entity<LavalandOutpostRuleComponent> ent,
        EntityUid mapUid,
        Vector2 origin,
        Box2 outpostBounds)
    {
        // Phase D — load the outpost grid for real. By now every rejected
        // dungeon grid has been deleted and the next physics tick has not yet
        // had a chance to interact with the outpost.
        if (!_loader.TryLoadGrid(
                ent.Comp.MapId ?? Transform(mapUid).MapID,
                ent.Comp.GridPath,
                out var loadedGrid,
                new DeserializationOptions { InitializeMaps = true }))
        {
            Log.Error($"Failed to load Lavaland outpost grid {ent.Comp.GridPath}.");
            return;
        }

        if (loadedGrid is not { } outpost)
        {
            Log.Error($"Lavaland outpost grid {ent.Comp.GridPath} loaded without a grid entity.");
            return;
        }

        ent.Comp.OutpostGrid = outpost.Owner;
        _meta.SetEntityName(outpost.Owner, Loc.GetString("lavaland-outpost-name"));

        // The outpost is authored as a shuttle. Keep it static so physics
        // cannot drift it away from the map centre over time.
        RemComp<ShuttleComponent>(outpost.Owner);
        if (TryComp<PhysicsComponent>(outpost.Owner, out var outpostPhysics))
        {
            _physics.SetBodyType(outpost.Owner, BodyType.Static, body: outpostPhysics);
            _physics.SetLinearVelocity(outpost.Owner, Vector2.Zero, body: outpostPhysics);
            _physics.SetAngularVelocity(outpost.Owner, 0f, body: outpostPhysics);
        }

        // Give the outpost dock a stable, localized name for the shuttle console.
        var dockLabel = EnsureComp<DockLabelComponent>(outpost.Owner);
        dockLabel.Label = "lavaland-outpost-dock-label";

        // Put the outpost in the middle of the planet. The source grid is not
        // necessarily centred around its own origin, so translate by the negation
        // of its local AABB centre first.
        var outpostXform = Transform(outpost.Owner);
        var correction = outpostXform.LocalPosition + outpost.Comp.LocalAABB.Center;
        _transform.SetLocalPosition(outpost.Owner, outpostXform.LocalPosition - correction, outpostXform);
        _transform.SetLocalRotation(outpost.Owner, Angle.Zero, outpostXform);

        // Re-reserve the actual outpost footprint now that we know the real
        // AABB after clearing any structure-side additions.
        ReserveOutpostArea(mapUid, outpost, outpostXform, ent.Comp.ClearRadius);

        // The beacon is parented to the outpost so it travels with the outpost if
        // the outpost is ever moved during an admin event.
        var ftlPoint = Spawn(ent.Comp.FtlPointProto, new EntityCoordinates(outpost.Owner, Vector2.Zero));
        _meta.SetEntityName(ftlPoint, Loc.GetString("lavaland-ftl-point"));

        // Structure loading can materialize additional outpost tiles. Correct
        // the centre once more and reserve the final footprint.
        var finalCorrection = outpostXform.LocalPosition + outpost.Comp.LocalAABB.Center;
        _transform.SetLocalPosition(outpost.Owner, outpostXform.LocalPosition - finalCorrection, outpostXform);
        ReserveOutpostArea(mapUid, outpost, outpostXform, ent.Comp.ClearRadius);
    }

    private void ApplyMarkerLayers(EntityUid mapUid, LavalandOutpostRuleComponent component)
    {
        if (!TryComp<BiomeComponent>(mapUid, out var biome))
            return;

        foreach (var marker in component.MarkerLayers)
            _biome.AddMarkerLayer(mapUid, biome, marker);
    }

    private void ApplyPlanetOverrides(EntityUid mapUid, LavalandOutpostRuleComponent component)
    {
        if (component.DisableLightCycle &&
            TryComp<LightCycleComponent>(mapUid, out var lightCycle))
        {
            lightCycle.Enabled = false;
            Dirty(mapUid, lightCycle);
        }

        if (component.RemoveRoof)
            RemComp<RoofComponent>(mapUid);

        if (TryComp<SunShadowComponent>(mapUid, out var sunShadow))
        {
            sunShadow.Direction = component.SunShadowDirection;
            sunShadow.Alpha = component.SunShadowAlpha;
            Dirty(mapUid, sunShadow);
        }

        if (TryComp<GravityComponent>(mapUid, out var gravity))
        {
            gravity.Enabled = component.GravityEnabled;
            gravity.Inherent = component.GravityInherent;
            Dirty(mapUid, gravity);
        }

        if (component.Atmosphere.Volume <= 0f)
        {
            Log.Error("Lavaland atmosphere has zero volume; keeping EnsurePlanet's default atmosphere.");
            return;
        }

        _atmos.SetMapAtmosphere(mapUid, false, component.Atmosphere.Clone());
    }

    private void ReserveOutpostArea(
        EntityUid mapUid,
        Entity<MapGridComponent> outpost,
        TransformComponent outpostXform,
        float clearRadius)
    {
        // ReserveTiles expects map-grid-local coordinates. The outpost grid has just
        // been centred, but this generic transform still accounts for any position or
        // rotation that a future map might apply.
        var localBounds = outpost.Comp.LocalAABB.Enlarged(clearRadius);
        var matrix = Matrix3x2.CreateRotation((float) outpostXform.LocalRotation) *
                     Matrix3x2.CreateTranslation(outpostXform.LocalPosition);
        var mapBounds = matrix.TransformBox(localBounds);

        _biome.ReserveTiles(mapUid, mapBounds, new List<(Vector2i, Tile)>());
    }
}
