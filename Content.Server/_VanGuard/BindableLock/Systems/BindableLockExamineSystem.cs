using Content.Shared._VanGuard.BindableLock.Components;
using Content.Shared.Access.Components;
using Content.Shared.Examine;

namespace Content.Server._VanGuard.BindableLock.Systems;

public sealed partial class BindableLockExamineSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BindableLockComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, BindableLockComponent component, ExaminedEvent args)
    {
        if (!TryComp<AccessReaderComponent>(uid, out var accessReader))
            return;

        if (accessReader.AccessLists.Count > 0 || accessReader.AccessKeys.Count > 0)
            return;

        if (!component.CanBind)
        {
            args.PushMarkup(Loc.GetString("examine-bindable-lock-bound"));
            return;
        }

        if (component.CanBind)
        {
            args.PushMarkup(Loc.GetString("examine-bindable-lock-unbound-hint"));
        }
    }
}