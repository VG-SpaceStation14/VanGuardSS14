using Content.Shared._VanGuard.PDA.Events;
using Content.Shared.PDA;

namespace Content.Client._VanGuard.PDA;

public sealed partial class PdaInteractionPreventionSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<PdaComponent, PdaCtrlClickEvent>(OnPdaCtrlClick);
    }

    private void OnPdaCtrlClick(Entity<PdaComponent> entity, ref PdaCtrlClickEvent args)
    {
        if (args.Handled)
            return;

        if (!IsPdaCarriedByUser(entity, args.User))
            return;

        args.Handled = true;
    }

    private bool IsPdaCarriedByUser(Entity<PdaComponent> entity, EntityUid user)
    {
        var currentParent = Transform(entity).ParentUid;
        var depth = 0;
        const int maxDepth = 10;

        while (currentParent.IsValid() && depth < maxDepth)
        {
            if (currentParent == user)
                return true;

            if (!TryComp(currentParent, out TransformComponent? parentTransform))
                break;

            currentParent = parentTransform.ParentUid;
            depth++;
        }

        return false;
    }
}