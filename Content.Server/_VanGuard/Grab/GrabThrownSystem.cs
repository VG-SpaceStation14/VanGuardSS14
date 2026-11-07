using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Item;
using Content.Shared._VanGuard.Grab;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Gravity;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._VanGuard.Grab;

/// <summary>
/// Applies the old grab-throw collision consequences to both the thrown target and whatever it hits.
/// </summary>
public sealed partial class GrabThrownSystem : EntitySystem
{
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private ThrownItemSystem _thrown = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly SoundPathSpecifier Thud = new("/Audio/Effects/thudswoosh.ogg");
    private static readonly SoundCollectionSpecifier MetalThud = new("MetalThud");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GrabThrownComponent, ThrowDoHitEvent>(OnHit);
        SubscribeLocalEvent<GrabThrownComponent, LandEvent>(OnLand);
    }

    private void OnHit(Entity<GrabThrownComponent> ent, ref ThrowDoHitEvent args)
    {
        if (!ent.Comp.HitEntities.Add(args.Target))
            return;

        if (TryComp<PhysicsComponent>(args.Target, out var hitPhysics) &&
            hitPhysics.BodyType == BodyType.Static)
        {
            _damage.TryChangeDamage(ent.Owner,
                new DamageSpecifier { DamageDict = { ["Blunt"] = 8 } }, origin: ent.Comp.Thrower);
            _stun.TryUpdateParalyzeDuration(ent.Owner, TimeSpan.FromSeconds(1));
            _audio.PlayPvs(MetalThud, ent.Owner);
        }

        // Old behavior: items skipped the body-collision effects.
        if (HasComp<ItemComponent>(ent.Owner))
            return;

        if (!HasComp<StaminaComponent>(args.Target) || _standing.IsDown(args.Target))
            return;

        _audio.PlayPvs(Thud, ent.Owner);
        _stun.TryUpdateParalyzeDuration(args.Target, TimeSpan.FromSeconds(2));
        _stamina.TakeStaminaDamage(args.Target, 65f);
        _stamina.TakeStaminaDamage(ent.Owner, 65f);
        _standing.Down(ent.Owner, playSound: false, dropHeldItems: false);

        ent.Comp.CollideCounter++;
        if (ent.Comp.CollideCounter < ent.Comp.MaxCollides)
            return;

        _stun.TryUpdateParalyzeDuration(ent.Owner, TimeSpan.FromSeconds(2));
        if (!_gravity.IsWeightless(ent.Owner) &&
            TryComp<PhysicsComponent>(ent.Owner, out var physics))
        {
            _physics.SetLinearVelocity(ent.Owner, Vector2.Zero, body: physics);
        }

        if (TryComp<ThrownItemComponent>(ent.Owner, out var thrown) &&
            TryComp<PhysicsComponent>(ent.Owner, out var thrownPhysics))
        {
            _thrown.LandComponent(ent.Owner, thrown, thrownPhysics, true);
        }
    }

    private void OnLand(Entity<GrabThrownComponent> ent, ref LandEvent args)
    {
        RemCompDeferred<GrabThrownComponent>(ent);
    }
}
