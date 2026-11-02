using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Loads a prepared grid as a shelter after a countdown. If the load fails the
/// capsule remains in place and the user is notified.
/// </summary>
public sealed partial class ShelterCapsuleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SmokeSystem _smoke = default!;
    [Dependency] private SharedShuttleSystem _shuttle = default!;
    [Dependency] private SharedMapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShelterCapsuleComponent, UseInHandEvent>(OnUse);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = AllEntityQuery<ShelterCapsuleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NextSpawn is not { } next || _timing.CurTime < next)
                continue;

            comp.NextSpawn = null;
            if (TrySpawn(uid, comp, out var error))
            {
                QueueDel(uid);
                continue;
            }

            _popup.PopupEntity(Loc.GetString("shelter-capsule-error", ("reason", error ?? "unknown")), uid, PopupType.LargeCaution);
        }
    }

    private void OnUse(Entity<ShelterCapsuleComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || ent.Comp.NextSpawn != null)
            return;

        args.Handled = true;
        ent.Comp.User = args.User;
        ent.Comp.NextSpawn = _timing.CurTime + ent.Comp.Delay;
        _popup.PopupPredicted(Loc.GetString("shelter-capsule-warning"), ent, args.User, PopupType.LargeCaution);
    }

    public bool TryDeploy(EntityUid capsule, ShelterCapsuleComponent? component = null)
    {
        if (!Resolve(capsule, ref component))
            return false;
        return TrySpawn(capsule, component, out _);
    }

    private bool TrySpawn(EntityUid capsule, ShelterCapsuleComponent comp, out string? error)
    {
        error = null;
        var xform = Transform(capsule);
        if (xform.MapID == MapId.Nullspace || !_map.TryGetMap(xform.MapID, out var mapUid))
        {
            error = "space";
            return false;
        }

        var nearby = _lookup.GetEntitiesInRange(xform.Coordinates, 2f);
        foreach (var uid in nearby)
        {
            if (uid == capsule ||
                uid == xform.GridUid ||
                uid == xform.MapUid ||
                uid == mapUid ||
                HasComp<MapGridComponent>(uid) ||
                HasComp<MapComponent>(uid))
            {
                continue;
            }

            if (comp.User == uid)
                continue;

            error = "obstructed";
            return false;
        }

        if (!_loader.TryLoadGrid(
                xform.MapID,
                comp.GridPath,
                out var loaded,
                new DeserializationOptions { InitializeMaps = true }) ||
            loaded is null)
        {
            error = "load";
            return false;
        }

        _transform.SetCoordinates(loaded.Value.Owner, new EntityCoordinates(mapUid.Value, xform.WorldPosition));
        _shuttle.AddIFFFlag(loaded.Value.Owner, IFFFlags.Hide);
        var smoke = Spawn("Smoke", xform.Coordinates);
        _smoke.StartSmoke(smoke, new Solution(), 3f, 12);
        return true;
    }
}
