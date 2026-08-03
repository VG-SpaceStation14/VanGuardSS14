using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Containers;
using Robust.Shared.Utility;

namespace Content.Shared.PDA
{
    public abstract partial class SharedPdaSystem : EntitySystem
    {
        [Dependency] protected ItemSlotsSystem ItemSlotsSystem = default!;
        [Dependency] protected SharedAppearanceSystem Appearance = default!;
        [Dependency] private SharedJobStatusSystem _jobStatus = default!;

        // VG-PDAScreens Start
        protected static readonly SpriteSpecifier ScreenOff = new SpriteSpecifier.Rsi(
            new ResPath("/Textures/_VanGuard/Objects/Devices/pda.rsi"),
            "pda_screen_borders");

        protected static readonly SpriteSpecifier ScreenMenu = new SpriteSpecifier.Rsi(
            new ResPath("/Textures/_VanGuard/Objects/Devices/pda.rsi"),
            "menu");
        // VG-PDAScreens End

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<PdaComponent, ComponentInit>(OnComponentInit);
            SubscribeLocalEvent<PdaComponent, ComponentRemove>(OnComponentRemove);

            SubscribeLocalEvent<PdaComponent, EntInsertedIntoContainerMessage>(OnItemInserted);
            SubscribeLocalEvent<PdaComponent, EntRemovedFromContainerMessage>(OnItemRemoved);

            SubscribeLocalEvent<PdaComponent, GetAdditionalAccessEvent>(OnGetAdditionalAccess);
        }
        protected virtual void OnComponentInit(EntityUid uid, PdaComponent pda, ComponentInit args)
        {
            if (pda.IdCard != null)
                pda.IdSlot.StartingItem = pda.IdCard;

            ItemSlotsSystem.AddItemSlot(uid, PdaComponent.PdaIdSlotId, pda.IdSlot);
            ItemSlotsSystem.AddItemSlot(uid, PdaComponent.PdaPenSlotId, pda.PenSlot);
            ItemSlotsSystem.AddItemSlot(uid, PdaComponent.PdaPaiSlotId, pda.PaiSlot);

            UpdatePdaAppearance(uid, pda);
        }

        private void OnComponentRemove(EntityUid uid, PdaComponent pda, ComponentRemove args)
        {
            ItemSlotsSystem.RemoveItemSlot(uid, pda.IdSlot);
            ItemSlotsSystem.RemoveItemSlot(uid, pda.PenSlot);
            ItemSlotsSystem.RemoveItemSlot(uid, pda.PaiSlot);
        }

        protected virtual void OnItemInserted(EntityUid uid, PdaComponent pda, EntInsertedIntoContainerMessage args)
        {
            if (args.Container.ID == PdaComponent.PdaIdSlotId)
                pda.ContainedId = args.Entity;

            UpdatePdaAppearance(uid, pda);
            UpdateJobStatus(uid);
        }

        protected virtual void OnItemRemoved(EntityUid uid, PdaComponent pda, EntRemovedFromContainerMessage args)
        {
            if (args.Container.ID == pda.IdSlot.ID)
                pda.ContainedId = null;

            UpdatePdaAppearance(uid, pda);
            UpdateJobStatus(uid);
        }

        private void OnGetAdditionalAccess(EntityUid uid, PdaComponent component, ref GetAdditionalAccessEvent args)
        {
            if (component.ContainedId is { } id)
                args.Entities.Add(id);
        }

        protected void UpdatePdaAppearance(EntityUid uid, PdaComponent pda) // VG-PDAScreens - private -> protected
        {
            Appearance.SetData(uid, PdaVisuals.IdCardInserted, pda.ContainedId != null);

            // VG-PDAScreens Start
            UpdatePdaScreen(uid);
            // VG-PDAScreens End

            // VG-PDA-Pen
            Appearance.SetData(uid, PdaVisuals.PenInserted, pda.PenSlot.HasItem);
        }

        private void UpdateJobStatus(EntityUid uid)
        {
            var parent = Transform(uid).ParentUid;
            _jobStatus.UpdateStatus(parent);
        }

        public virtual void UpdatePdaUi(EntityUid uid, PdaComponent? pda = null)
        {
            // This does nothing yet while I finish up PDA prediction
            // Overriden by the server
        }

        // VG-PDAScreens Start
        public virtual void UpdatePdaScreen(EntityUid uid, SpriteSpecifier? screenState = null)
        {
            if (!TryComp<PdaComponent>(uid, out var pda))
                return;

            if (!pda.Powered)
            {
                Appearance.SetData(uid, PdaVisuals.ScreenState, ScreenOff);
                return;
            }

            if (screenState != null)
            {
                Appearance.SetData(uid, PdaVisuals.ScreenState, screenState);
                return;
            }

            if (TryComp<CartridgeLoader.CartridgeLoaderComponent>(uid, out var loader) && loader.ActiveProgram != null)
            {
                if (TryComp<CartridgeLoader.CartridgeComponent>(loader.ActiveProgram, out var cartridge) && cartridge.ScreenState != null)
                {
                    Appearance.SetData(uid, PdaVisuals.ScreenState, cartridge.ScreenState);
                    return;
                }
            }

            Appearance.SetData(uid, PdaVisuals.ScreenState, ScreenMenu);
        }
        // VG-PDAScreens End
    }
}