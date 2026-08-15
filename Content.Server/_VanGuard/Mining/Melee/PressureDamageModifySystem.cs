using Content.Server.Atmos.EntitySystems;
using Content.Server.Popups;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._VanGuard.Mining.Melee;

/// <summary>
/// Server-side implementation of <see cref="PressureDamageModifyComponent"/>.
/// </summary>
public sealed partial class PressureDamageModifySystem : EntitySystem
{
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PressureDamageModifyComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<PressureDamageModifyComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnProjectileHit(EntityUid uid, PressureDamageModifyComponent component, ref ProjectileHitEvent args)
    {
        // Low/no pressure reduces projectile effectiveness.
        var pressure = GetPressureAt(args.Target);
        if (!IsPressureInRange(pressure, component))
            args.Damage *= component.ProjDamage;
    }

    private void OnMeleeHit(EntityUid uid, PressureDamageModifyComponent component, ref MeleeHitEvent args)
    {
        if (!args.IsHit || component.AdditionalDamage == null)
            return;

        foreach (var target in args.HitEntities)
        {
            var pressure = GetPressureAt(target);
            if (IsPressureInRange(pressure, component))
                _damage.TryChangeDamage(target, component.AdditionalDamage);
        }
    }

    private float GetPressureAt(EntityUid entity)
    {
        if (_atmosphere.GetContainingMixture(entity) is { } mixture)
            return MathF.Max(mixture.Pressure, 1f);

        return 1f;
    }

    private bool IsPressureInRange(float pressure, PressureDamageModifyComponent component)
        => pressure >= component.MinPressure && pressure <= component.MaxPressure;
}

