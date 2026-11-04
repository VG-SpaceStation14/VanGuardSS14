using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Weapons.Melee;

namespace Content.Server._VanGuard.Lavaland;

public sealed partial class LavalandSwitchableWeaponSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LavalandSwitchableWeaponComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<LavalandSwitchableWeaponComponent, ItemToggledEvent>(OnToggled);
    }

    private void OnStartup(Entity<LavalandSwitchableWeaponComponent> ent, ref ComponentStartup args)
    {
        var activated = TryComp<ItemToggleComponent>(ent, out var toggle) && toggle.Activated;
        Apply(ent, activated);
    }

    private void OnToggled(Entity<LavalandSwitchableWeaponComponent> ent, ref ItemToggledEvent args)
    {
        Apply(ent, args.Activated);
    }

    private void Apply(Entity<LavalandSwitchableWeaponComponent> ent, bool activated)
    {
        if (!TryComp<MeleeWeaponComponent>(ent, out var melee))
            return;

        melee.AttackRate = activated
            ? ent.Comp.AttackRateActivated
            : ent.Comp.AttackRateDeactivated;
        Dirty(ent, melee);
    }
}
