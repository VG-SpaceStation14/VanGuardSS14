using Robust.Shared.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;
using Content.Shared.Chasm;

namespace Content.Server._VanGuard.Lavaland;

public sealed partial class LavalandFeatureSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LavalandOreDepositComponent, MapInitEvent>(OnOreInit);
        SubscribeLocalEvent<LavalandBiomeFeatureComponent, MapInitEvent>(OnFeatureInit);
    }

    private void OnOreInit(Entity<LavalandOreDepositComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Ores.Count == 0)
            return;

        Spawn(_random.Pick(ent.Comp.Ores), Transform(ent.Owner).Coordinates);
        QueueDel(ent.Owner);
    }

    private void OnFeatureInit(Entity<LavalandBiomeFeatureComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Options.Count == 0)
            return;

        var xform = Transform(ent.Owner);
        var chosen = xform.Coordinates;

        for (var attempt = 0; attempt < 12; attempt++)
        {
            var candidate = xform.Coordinates.Offset(_random.NextVector2(2f));
            if (IsValid(candidate, ent.Owner))
            {
                chosen = candidate;
                break;
            }

            if (attempt == 11)
            {
                QueueDel(ent.Owner);
                return;
            }
        }

        Spawn(_random.Pick(ent.Comp.Options), chosen);
        QueueDel(ent.Owner);
    }

    private bool IsValid(EntityCoordinates coordinates, EntityUid marker)
    {
        if (!_turf.TryGetTileRef(coordinates, out var tile) || tile.Value.Tile.IsEmpty)
            return false;

        if (_turf.IsTileBlocked(tile.Value, CollisionGroup.MobMask))
            return false;

        foreach (var uid in _lookup.GetEntitiesInRange(coordinates, 0.6f))
        {
            if (uid == marker)
                continue;

            if (HasComp<ChasmComponent>(uid))
                return false;

            if (TryComp(uid, out MetaDataComponent? meta) &&
                meta.EntityPrototype?.ID == "FloorLavaEntity")
                return false;
        }

        return true;
    }
}
