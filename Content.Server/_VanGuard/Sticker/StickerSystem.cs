using Content.Shared.Access.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared._VanGuard.Sticker;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Sticker;

public sealed partial class StickerSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IdCardComponent, ComponentInit>(OnIdCardInit);
        SubscribeLocalEvent<IdCardComponent, InteractUsingEvent>(OnIdCardInteractUsing);
        SubscribeLocalEvent<EntRemovedFromContainerMessage>(OnEntRemoved);
    }

    private void OnIdCardInit(EntityUid uid, IdCardComponent card, ComponentInit args)
    {
        UpdateCardAppearance(uid, card);
    }

    private void UpdateCardAppearance(EntityUid uid, IdCardComponent card)
    {
        if (!TryComp<ItemSlotsComponent>(uid, out var itemSlots)
            || !_itemSlots.TryGetSlot(uid, "sticker", out var slot))
            return;

        if (slot.Item is { } item && TryComp<StickerComponent>(item, out var sticker))
            _appearance.SetData(uid, IdCardVisuals.StickerOverlay, sticker.OverlayState);
        else
            _appearance.SetData(uid, IdCardVisuals.StickerOverlay, "");
    }

    private void OnIdCardInteractUsing(EntityUid uid, IdCardComponent card, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (TryComp<StickerComponent>(args.Used, out var sticker))
        {
            if (!TryComp<ItemSlotsComponent>(uid, out var itemSlots)
                || !_itemSlots.TryGetSlot(uid, "sticker", out var slot))
                return;

            if (slot.HasItem && slot.ContainerSlot != null && slot.Item != null)
            {
                var oldSticker = slot.Item.Value;
                var user = args.User;
                
                _container.Remove(oldSticker, slot.ContainerSlot);

                Timer.Spawn(TimeSpan.FromMilliseconds(50), () =>
                {
                    if (!Deleted(oldSticker) && !Deleted(user) && HasComp<HandsComponent>(user))
                        _handsSystem.TryPickupAnyHand(user, oldSticker);
                });
            }

            if (!_container.Insert(args.Used, slot.ContainerSlot!))
                return;

            args.Handled = true;
            _appearance.SetData(uid, IdCardVisuals.StickerOverlay, sticker.OverlayState);
        }
    }

    private void OnEntRemoved(EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != "sticker")
            return;

        if (!TryComp<IdCardComponent>(args.Container.Owner, out _))
            return;

        _appearance.SetData(args.Container.Owner, IdCardVisuals.StickerOverlay, "");
    }

    public void SetSticker(EntityUid cardUid, EntityUid? stickerUid, IdCardComponent? card = null)
    {
        if (!Resolve(cardUid, ref card))
            return;

        if (!TryComp<ItemSlotsComponent>(cardUid, out var itemSlots)
            || !_itemSlots.TryGetSlot(cardUid, "sticker", out var slot))
            return;

        if (slot.HasItem && slot.ContainerSlot != null && slot.Item != null)
            _container.Remove(slot.Item.Value, slot.ContainerSlot);

        if (stickerUid != null && slot.ContainerSlot != null)
            _container.Insert(stickerUid.Value, slot.ContainerSlot);

        UpdateCardAppearance(cardUid, card);
    }
}