using Content.Shared._VanGuard.Weather;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Weather;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Weather;

/// <summary>
/// Applies heat damage to exposed mobs during the damaging ashfall stage.
/// The visual/audio weather itself is the stock WeatherStatusEffect.
/// </summary>
public sealed partial class LavalandStormEffectsSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedWeatherSystem _weather = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly ProtoId<DamageTypePrototype> Heat = "Heat";
    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(2);
        var heat = _proto.Index(Heat);

        var weatherQuery = AllEntityQuery<WeatherStatusEffectComponent, TransformComponent>();
        var maps = new HashSet<EntityUid>();
        while (weatherQuery.MoveNext(out _, out _, out var wxform))
        {
            if (wxform.MapUid is { } mapUid)
                maps.Add(mapUid);
        }

        foreach (var mapUid in maps)
        {
            if (!TryComp<MapComponent>(mapUid, out var map))
                continue;

            if (!_weather.HasWeather(map.MapId, "WeatherAshfall"))
                continue;

            var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
            while (mobs.MoveNext(out var uid, out var mob, out var xform))
            {
                if (xform.MapUid != mapUid || mob.CurrentState == MobState.Dead)
                    continue;

                if (HasComp<LavalandAshStormImmuneComponent>(uid))
                    continue;

                if (xform.GridUid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var grid))
                {
                    var tile = _map.GetTileRef((gridUid, grid), xform.Coordinates);
                    if (!_weather.CanWeatherAffect((gridUid, grid, null), tile))
                        continue;
                }

                _damageable.TryChangeDamage(uid, new DamageSpecifier(heat, 4), interruptsDoAfters: false);
            }
        }
    }
}
