using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Shared.GameTicking;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Shuttles.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
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

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LavalandOutpostRuleComponent, RuleLoadedGridsEvent>(OnRuleLoadedGrids);
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

        // Load the outpost after EnsurePlanet. EnsurePlanet disables shuttle physics
        // on every grid that already exists; loading afterwards keeps the outpost
        // untouched by that side effect.
        if (!_loader.TryLoadGrid(
                args.Map,
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

        // Put the outpost in the middle of the planet. The source grid is not
        // necessarily centred around its own origin, so translate by the negation
        // of its local AABB centre first.
        var outpostXform = Transform(outpost.Owner);
        _transform.SetLocalPosition(outpost.Owner, -outpost.Comp.LocalAABB.Center, outpostXform);
        _transform.SetLocalRotation(outpost.Owner, Angle.Zero, outpostXform);

        ReserveOutpostArea(mapUid, outpost, outpostXform, ent.Comp.ClearRadius);

        var destination = EnsureComp<FTLDestinationComponent>(mapUid);
        destination.Enabled = true;
        destination.BeaconsOnly = false;
        destination.RequireCoordinateDisk = false;
        destination.Whitelist = null;
        Dirty(mapUid, destination);

        // The beacon is parented to the outpost so it travels with the outpost if
        // the outpost is ever moved during an admin event.
        var ftlPoint = Spawn(ent.Comp.FtlPointProto, new EntityCoordinates(outpost.Owner, Vector2.Zero));
        _meta.SetEntityName(ftlPoint, Loc.GetString("lavaland-ftl-point"));
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
