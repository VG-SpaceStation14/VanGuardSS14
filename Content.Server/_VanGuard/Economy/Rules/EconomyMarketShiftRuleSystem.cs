using System.Linq;
using Content.Server._VanGuard.Economy.Rules.Components;
using Content.Server._VanGuard.Economy.Systems;
using Content.Server.GameTicking.Rules;
using Content.Shared._VanGuard.Economy.Prototypes;
using Content.Shared.GameTicking.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Economy.Rules;

/// <summary>
///     Periodically shifts commodity sell prices on all stations until the rule ends.
/// </summary>
public sealed partial class EconomyMarketShiftRuleSystem : GameRuleSystem<EconomyMarketShiftRuleComponent>
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EconomyMarketSystem _market = default!;

    protected override void Started(EntityUid uid, EconomyMarketShiftRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        ScheduleNextShift(component);
    }

    protected override void ActiveTick(EntityUid uid, EconomyMarketShiftRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (_timing.CurTime < component.NextShiftTime)
            return;

        ApplyShiftToAllStations(component);
        ScheduleNextShift(component);
    }

    protected override void Ended(EntityUid uid, EconomyMarketShiftRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        var stations = EntityQueryEnumerator<StationDataComponent>();
        while (stations.MoveNext(out var stationUid, out _))
        {
            _market.ClearMarketModifiers(stationUid);
        }
    }

    private void ApplyShiftToAllStations(EconomyMarketShiftRuleComponent component)
    {
        var stations = EntityQueryEnumerator<StationDataComponent>();
        while (stations.MoveNext(out var stationUid, out _))
        {
            ApplyShift(stationUid, component);
        }
    }

    private void ApplyShift(EntityUid stationUid, EconomyMarketShiftRuleComponent component)
    {
        var commodities = _proto.EnumeratePrototypes<MarketCommodityPrototype>().ToList();
        if (component.AllowedMaterials is { Count: > 0 })
            commodities = commodities.Where(c => component.AllowedMaterials.Contains(c.Material)).ToList();

        if (commodities.Count == 0)
        {
            _market.ClearMarketModifiers(stationUid);
            return;
        }

        var increasedMin = Math.Min(component.IncreasedMultiplierMin, component.IncreasedMultiplierMax);
        var increasedMax = Math.Max(component.IncreasedMultiplierMin, component.IncreasedMultiplierMax);
        var decreasedMin = Math.Min(component.DecreasedMultiplierMin, component.DecreasedMultiplierMax);
        var decreasedMax = Math.Max(component.DecreasedMultiplierMin, component.DecreasedMultiplierMax);

        var increasedCount = Math.Clamp(_random.Next(Math.Min(component.MinIncreased, component.MaxIncreased),
            Math.Max(component.MinIncreased, component.MaxIncreased) + 1), 0, commodities.Count);

        var picked = new List<MarketCommodityPrototype>(commodities);
        var increased = new List<string>();
        var decreased = new List<string>();
        var modifiers = new Dictionary<string, float>();

        for (var i = 0; i < increasedCount; i++)
        {
            var item = _random.PickAndTake(picked);
            modifiers[item.Material] = NextIncreasedMultiplier(increasedMin, increasedMax);
            increased.Add(item.Material);
        }

        var maxDecreasedPool = Math.Max(0, picked.Count);
        var decreasedCount = Math.Clamp(_random.Next(Math.Min(component.MinDecreased, component.MaxDecreased),
            Math.Max(component.MinDecreased, component.MaxDecreased) + 1), 0, maxDecreasedPool);

        for (var i = 0; i < decreasedCount; i++)
        {
            var item = _random.PickAndTake(picked);
            modifiers[item.Material] = NextDecreasedMultiplier(decreasedMin, decreasedMax);
            decreased.Add(item.Material);
        }

        _market.SetMarketModifiers(stationUid, modifiers);
        if (component.AnnouncementsEnabled)
            _market.AnnounceMarketChanges(stationUid, increased, decreased);
    }

    private float NextIncreasedMultiplier(float min, float max)
    {
        var low = MathF.Max(min, 1f);
        var high = MathF.Max(max, low);
        return MathF.Max(_random.NextFloat(low, high), 1.01f);
    }

    private float NextDecreasedMultiplier(float min, float max)
    {
        var low = MathF.Min(min, 0.99f);
        var high = MathF.Min(max, 1f);
        if (high <= low)
            return low;

        return MathF.Min(_random.NextFloat(low, high), 0.99f);
    }

    private void ScheduleNextShift(EconomyMarketShiftRuleComponent component)
    {
        var minSeconds = MathF.Min((float)component.MinInterval.TotalSeconds, (float)component.MaxInterval.TotalSeconds);
        var maxSeconds = MathF.Max((float)component.MinInterval.TotalSeconds, (float)component.MaxInterval.TotalSeconds);
        var seconds = _random.NextFloat(minSeconds, maxSeconds);

        component.NextShiftTime = _timing.CurTime + TimeSpan.FromSeconds(seconds);
    }
}
