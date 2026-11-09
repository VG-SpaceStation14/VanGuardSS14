using Content.Shared._VanGuard.Carrying;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;

namespace Content.Client._VanGuard.Carrying;

/// <summary>
/// Suppresses ordinary hand interactions on the client when the networked
/// carry mode will turn the click into a pickup request on the server.
/// </summary>
public sealed class CarryModeInteractionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CarryModeComponent, BeforeInteractHandEvent>(OnBeforeInteractHand);
    }

    private void OnBeforeInteractHand(Entity<CarryModeComponent> mode, ref BeforeInteractHandEvent args)
    {
        if (!mode.Comp.Enabled || args.Handled || args.Target == mode.Owner)
            return;

        if (!HasComp<HumanoidProfileComponent>(args.Target))
            return;

        // Do not start carrying here. The server owns CanStartCarry/DoAfter.
        args.Handled = true;
    }
}
