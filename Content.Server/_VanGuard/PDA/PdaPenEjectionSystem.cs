using Content.Shared._VanGuard.PDA.Events;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;

namespace Content.Server._VanGuard.PDA;

public sealed partial class PdaPenEjectionSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _slotManager = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PdaComponent, PdaCtrlClickEvent>(OnPdaCtrlClick);
    }

    private void OnPdaCtrlClick(Entity<PdaComponent> entity, ref PdaCtrlClickEvent args)
    {
        if (args.Handled)
            return;

        if (!IsPdaOwnedByUser(entity, args.User))
            return;

        var slot = entity.Comp.PenSlot;
        if (slot.HasItem)
        {
            _slotManager.TryEjectToHands(entity, slot, args.User);
        }

        args.Handled = true;
    }

    private bool IsPdaOwnedByUser(Entity<PdaComponent> entity, EntityUid potentialOwner)
    {
        var currentParent = Transform(entity).ParentUid;
        var traverseDepth = 0;
        const int maxDepth = 10;

        while (currentParent.IsValid() && traverseDepth < maxDepth)
        {
            if (currentParent == potentialOwner)
                return true;

            if (!TryComp(currentParent, out TransformComponent? parentTransform))
                break;

            currentParent = parentTransform.ParentUid;
            traverseDepth++;
        }

        return false;
    }
}