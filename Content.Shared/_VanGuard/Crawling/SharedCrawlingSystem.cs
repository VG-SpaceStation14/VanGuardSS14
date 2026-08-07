using Content.Shared.Standing;
using Content.Shared.Climbing.Events;
using Content.Shared.Explosion;
using Content.Shared.Stunnable;

namespace Content.Shared._VanGuard.Crawling;

public abstract partial class SharedCrawlingSystem : EntitySystem
{
    [Dependency] private SharedStunSystem _stun = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StandingStateComponent, GetExplosionResistanceEvent>(OnExplosionKnockdown);
        SubscribeLocalEvent<StandingStateComponent, AttemptClimbEvent>(PreventClimbingWhileDown);
    }

    private void OnExplosionKnockdown(EntityUid uid, StandingStateComponent component, GetExplosionResistanceEvent args)
    {
        var duration = component.Standing ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(1);
        _stun.TryKnockdown(uid, duration, true);
    }

    private void PreventClimbingWhileDown(EntityUid uid, StandingStateComponent comp, ref AttemptClimbEvent args)
    {
        if (args.Cancelled || comp.Standing)
            return;

        args.Cancelled = true;
    }
}