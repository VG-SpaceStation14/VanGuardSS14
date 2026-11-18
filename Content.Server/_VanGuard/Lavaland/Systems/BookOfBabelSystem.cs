using Content.Server._VanGuard.Language;
using Content.Server._VanGuard.Lavaland.Components;
using Content.Shared._VanGuard.Language;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Lavaland.Systems;

public sealed partial class BookOfBabelSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private LanguageSystem _language = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BookOfBabelComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(Entity<BookOfBabelComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var user = args.User;

        foreach (var proto in _proto.EnumeratePrototypes<LanguagePrototype>())
        {
            if (proto.Abstract)
                continue;

            if (proto.ID == SharedLanguageSystem.UniversalLanguageId)
                continue;

            _language.GrantLanguage(user, proto.ID, LanguageKnowledge.Speak);
        }

        _popup.PopupEntity(
            Loc.GetString("book-of-babel-used"),
            user,
            user,
            PopupType.LargeCaution);

        // Replace the book with ash in the user's hand.
        if (_hands.TryGetActiveItem(user, out var activeItem) && activeItem == ent.Owner)
        {
            _hands.TryDrop(user, ent.Owner);
            var ash = Spawn("Ash", Transform(user).Coordinates);
            _hands.TryForcePickupAnyHand(user, ash);
        }
        else
        {
            Spawn("Ash", Transform(ent.Owner).Coordinates);
        }

        QueueDel(ent.Owner);
    }
}
