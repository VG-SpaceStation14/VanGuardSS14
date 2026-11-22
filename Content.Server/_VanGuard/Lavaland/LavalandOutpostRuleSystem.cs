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

        // Phase C — VG-Tweak: the outpost is now the very last generation
        // step, running synchronously after POIs, dungeon merges, unique
        // structures and the shuttle-physics sweep have all finished. Earlier
        // versions deferred this to the next tick via a queued event, which
        // let biome chunks, physics and other tick work start before the
        // outpost existed; because the outpost is large enough to briefly
        // share world space with a transient dungeon grid that had not been
        // deleted yet, its contents ended up reparented onto those extra
        // grids and vanished. Running it here, with the transient grids
        // already deleted and the footprint cleared, removes that window.
        LoadOutpostGrid(ent, mapUid, origin, outpostBounds);

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
        var measureMapUid = _map.CreateMap(out var measureMapId);
        try
        {
            if (!_loader.TryLoadGrid(
                    measureMapId,
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
            // VG-Tweak: delete the measurement map immediately, not via
            // QueueDeleteMap. The queued delete only lands at the start of the
            // next tick, so the throwaway grid is still alive and still in the
            // broadphase while the real outpost is being loaded below. The
            // outpost is large enough to briefly share world space with it,
            // which is how outpost content ends up parented to an extra
            // throwaway grid. DeleteMap goes through Del -> DeleteEntity
            // immediately, so nothing is left to collide with.
            _map.DeleteMap(measureMapId);
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
        //
        // VG-Tweak: clear the footprint first. The outpost is large enough to
        // briefly share world space with any leftover grid or straggler that
        // a previous generation step left behind, and content loaded on top
        // of such a grid can be reparented onto it. Guaranteeing the area is
        // empty removes that whole class of bugs.
        ClearOutpostFootprint(mapUid, outpostBounds);

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

        // VG-Tweak: verify that the outpost content landed on the outpost grid
        // and not on any leftover grid in the footprint area. A single missing
        // thing in this list means a runtime error in TryLoadGrid or in
        // entity parenting.
        VerifyOutpostContent(outpost, ent.Comp.GridPath);
    }

    /// <summary>
    ///     VG-Tweak: removes leftover grids that intersect the outpost's
    ///     future footprint. The outpost is too large to load on top of
    ///     anything else; if a stray grid is left there from an earlier
    ///     generation step, the loader will happily reparent outpost content
    ///     onto it and the content ends up in the wrong place.
    /// </summary>
    private void ClearOutpostFootprint(EntityUid mapUid, Box2 footprint)
    {
        var strays = new List<EntityUid>();
        var gridQuery = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var gridUid, out var grid, out var xform))
        {
            if (xform.MapID != Transform(mapUid).MapID || gridUid == mapUid)
                continue;

            if (!footprint.Intersects(grid.LocalAABB.Translated(xform.LocalPosition)))
                continue;

            strays.Add(gridUid);
        }

        foreach (var stray in strays)
        {
            Log.Warning($"Lavaland outpost footprint overlaps grid {stray}; removing it before loading the outpost.");
            Del(stray);
        }
    }

    /// <summary>
    /// Checks that the freshly loaded outpost is in a consistent state: every
    /// entity in the footprint is parented to the outpost grid, has a valid
    /// GridUid and carries its essential components. Logs a warning on any
    /// deviation so the CI and dev consoles surface it immediately.
    /// </summary>
    private void VerifyOutpostContent(Entity<MapGridComponent> outpost, ResPath gridPath)
    {
        var orphaned = 0;
        var total = 0;
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (uid == outpost.Owner)
                continue;

            // The verification is limited to entities whose GridUid is the
            // outpost or whose direct parent is the outpost. Anything deeper
            // (containers, storage) is not checked here.
            if (xform.GridUid != outpost.Owner)
                continue;

            total++;
            if (xform.ParentUid != outpost.Owner &&
                !IsOutpostAncestor(outpost.Owner, xform.ParentUid))
            {
                orphaned++;
                Log.Warning($"Lavaland outpost content {uid} at {xform.WorldPosition} is parented to {xform.ParentUid} instead of the outpost grid ({gridPath}).");
            }
        }

        if (orphaned > 0)
            Log.Warning($"Lavaland outpost {gridPath}: {orphaned} of {total} contents are parented to a foreign grid.");
    }

    private bool IsOutpostAncestor(EntityUid outpost, EntityUid node)
    {
        var depth = 0;
        while (node.IsValid() && depth++ < 8)
        {
            if (node == outpost)
                return true;

            if (!TryComp<TransformComponent>(node, out var xform))
                return false;

            node = xform.ParentUid;
        }

        return false;
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
