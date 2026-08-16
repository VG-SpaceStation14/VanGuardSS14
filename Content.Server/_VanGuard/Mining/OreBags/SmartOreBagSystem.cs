using Content.Shared._VanGuard.Mining.OreBags;
using Content.Shared.Interaction;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Server._VanGuard.Mining.OreBags;

/// <summary>
/// Server-side logic for smart ore bags: opens the filter window and applies
/// the ignore list sent back by the client.
/// </summary>
public sealed partial class SmartOreBagSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SmartOreBagComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<SmartOreBagComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeNetworkEvent<SmartOreBagUpdateMessage>(OnUpdateIgnored);
    }

    private void OnGetVerbs(Entity<SmartOreBagComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        var verb = new Verb
        {
            Text = Loc.GetString("smart-ore-bag-verb"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/settings.svg.192dpi.png")),
            Act = () => OpenConfigWindow(ent, user),
            Priority = 2
        };
        args.Verbs.Add(verb);
    }

    private void OnInteractUsing(Entity<SmartOreBagComponent> ent, ref InteractUsingEvent args)
    {
        OpenConfigWindow(ent, args.User);
        args.Handled = true;
    }

    private void OpenConfigWindow(Entity<SmartOreBagComponent> ent, EntityUid user)
    {
        var msg = new OpenSmartOreBagWindowMessage(GetNetEntity(ent.Owner), ent.Comp.IgnoredOres);
        RaiseNetworkEvent(msg, user);
    }

    private void OnUpdateIgnored(SmartOreBagUpdateMessage msg, EntitySessionEventArgs args)
    {
        var uid = GetEntity(msg.Entity);

        if (!TryComp<SmartOreBagComponent>(uid, out var component))
            return;

        component.IgnoredOres = msg.IgnoredOres;
        Dirty(uid, component);
    }
}
