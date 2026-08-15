using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._VanGuard.Mining.Melee;

/// <summary>
/// Applies bonus damage from <see cref="BackstabDamageMultipilierComponent"/> when the
/// attacker is roughly behind the target.
/// </summary>
public sealed partial class BackstabDamageMultipilierSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BackstabDamageMultipilierComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(Entity<BackstabDamageMultipilierComponent> ent, ref MeleeHitEvent args)
    {
        foreach (var damaged in args.HitEntities)
        {
            if (damaged == args.User)
                continue;

            // Counts as a backstab when the attacker is roughly behind the target.
            var attackerPos = Transform(args.User).LocalPosition;
            var targetPos = Transform(damaged).LocalPosition;
            var toAttacker = attackerPos - targetPos;

            if (toAttacker == Vector2.Zero)
                continue;

            var attackerAngle = toAttacker.ToAngle();
            var diff = (attackerAngle - Transform(damaged).LocalRotation).Reduced();
            if (Math.Abs(diff.Degrees) < 90)
                _damageable.TryChangeDamage(damaged, ent.Comp.BonusDamage, origin: args.User);
        }
    }
}

