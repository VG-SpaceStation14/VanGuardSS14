using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;

namespace Content.Server._VanGuard.Lavaland;

public sealed partial class LavalandFeatureSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private TurfSystem _turf = default!;

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
        if (xform.GridUid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var grid))
        {
            var tile = _turf.GetTileRef(xform.Coordinates);
            if (tile is { } && _turf.IsTileBlocked(tile.Value, CollisionGroup.MobMask))
            {
                QueueDel(ent.Owner);
                return;
            }
        }

        Spawn(_random.Pick(ent.Comp.Options), xform.Coordinates);
        QueueDel(ent.Owner);
    }
}
