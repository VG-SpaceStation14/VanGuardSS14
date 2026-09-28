using Content.Server._VanGuard.Economy.Rules.Components;
using Content.Server._VanGuard.Economy.Systems;
using Content.Shared.GameTicking.Components;
using Content.Shared.GameTicking.Rules;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Economy.Rules;

/// <summary>
///     Periodically pays every employed crew member their salary.
/// </summary>
public sealed partial class EconomyPaydayRuleSystem : GameRuleSystem<EconomyPaydayRuleComponent>
{
    [Dependency] private EconomyPayrollSystem _payroll = default!;
    [Dependency] private IGameTiming _timing = default!;

    protected override void Started(Entity<EconomyPaydayRuleComponent, GameRuleComponent> rule, ref GameRuleStartedEvent args)
    {
        base.Started(rule, ref args);

        rule.Comp1.NextPayday = _timing.CurTime + rule.Comp1.Interval;
    }

    protected override void ActiveTick(EntityUid uid, EconomyPaydayRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (_timing.CurTime < component.NextPayday)
            return;

        component.NextPayday = _timing.CurTime + component.Interval;
        _payroll.ProcessPayroll();
    }
}
