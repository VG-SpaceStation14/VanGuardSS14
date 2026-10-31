using System.Linq;
using Content.Shared.Body.Events;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared._VanGuard.Quirks.Components;
using Content.Shared.Movement.Systems;

namespace Content.Shared._VanGuard.Quirks.Systems;

/// <summary>
///     Applies the passive effects of the character quirks that are represented by simple modifiers.
///     The quirk components are added to a mob when it spawns with the corresponding quirk selected.
/// </summary>
public sealed partial class QuirkEffectsSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<QuirkMovementSpeedComponent, ComponentStartup>(OnMovementSpeedStartup);
        SubscribeLocalEvent<QuirkMovementSpeedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);

        SubscribeLocalEvent<QuirkDamageTypeResistanceComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<QuirkBleedRateComponent, BleedModifierEvent>(OnBleedModify);
    }

    private void OnMovementSpeedStartup(Entity<QuirkMovementSpeedComponent> ent, ref ComponentStartup args)
    {
        // Make sure the modifier is taken into account right away instead of waiting for the next refresh.
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnRefreshMovementSpeed(Entity<QuirkMovementSpeedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.WalkModifier, ent.Comp.SprintModifier);
    }

    private void OnDamageModify(Entity<QuirkDamageTypeResistanceComponent> ent, ref DamageModifyEvent args)
    {
        foreach (var damageType in args.Damage.DamageDict.Keys.ToArray())
        {
            if (ent.Comp.Modifiers.TryGetValue(damageType.Id, out var modifier))
                args.Damage.DamageDict[damageType] *= modifier;
        }
    }

    private void OnBleedModify(Entity<QuirkBleedRateComponent> ent, ref BleedModifierEvent args)
    {
        args.BleedAmount *= ent.Comp.BleedMultiplier;
    }
}
