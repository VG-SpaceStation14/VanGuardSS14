using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared._VanGuard.Movement.Components;

namespace Content.Server._VanGuard.Movement;

public sealed partial class VanGuardMovementSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VanGuardMovementComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<VanGuardMovementComponent> ent, ref MapInitEvent args)
    {
        var uid = ent.Owner;

        if (TryComp<MovementSpeedModifierComponent>(uid, out var moveComp))
        {
            (moveComp.BaseSprintSpeed, moveComp.BaseWalkSpeed) = 
                (moveComp.BaseWalkSpeed, moveComp.BaseSprintSpeed);
            
            Dirty(uid, moveComp);
        }
    }
}