using Content.Shared._VanGuard.Grab;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;

namespace Content.Server._VanGuard.Grab;

/// <summary>
/// Makes a target thrown by a hard/choke grab fall when it hits something or lands.
/// </summary>
public sealed partial class GrabThrownSystem : EntitySystem
{
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GrabThrownComponent, ThrowDoHitEvent>(OnHit);
        SubscribeLocalEvent<GrabThrownComponent, LandEvent>(OnLand);
    }

    private void OnHit(Entity<GrabThrownComponent> ent, ref ThrowDoHitEvent args)
    {
        if (args.Target == ent.Comp.Thrower)
            return;

        MakeFall(ent);
    }

    private void OnLand(Entity<GrabThrownComponent> ent, ref LandEvent args)
    {
        MakeFall(ent);
    }

    private void MakeFall(Entity<GrabThrownComponent> ent)
    {
        if (!HasComp<MobStateComponent>(ent.Owner) || _mobState.IsDead(ent.Owner))
        {
            RemCompDeferred<GrabThrownComponent>(ent);
            return;
        }

        if (!_standing.IsDown(ent.Owner))
            _stun.TryKnockdown(ent.Owner, TimeSpan.FromSeconds(2), autoStand: true, drop: true, force: true);

        RemCompDeferred<GrabThrownComponent>(ent);
    }
}
