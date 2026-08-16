using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tag;

namespace Content.Shared._VanGuard.Mining.Materials;

/// <summary>
/// Implements the "use ore bag on ore processor" behaviour: everything stored in the
/// used container is inserted into the machine's material storage at once.
/// </summary>
public abstract partial class SharedAutoMaterialInsertSystem : EntitySystem
{
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private SharedMaterialStorageSystem _materialStorage = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AutoMaterialInsertComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnInteractUsing(EntityUid uid, AutoMaterialInsertComponent comp, InteractUsingEvent args)
    {
        var used = args.Used;

        if (!_tag.HasTag(used, comp.Tag) || !TryComp<StorageComponent>(used, out var storage))
            return;

        var stored = new List<EntityUid>(storage.StoredItems.Count);
        foreach (var item in storage.StoredItems.Keys)
        {
            stored.Add(item);
        }

        var inserted = false;
        foreach (var item in stored)
        {
            if (_materialStorage.TryInsertMaterialEntity(args.User, item, uid))
                inserted = true;
        }

        if (inserted)
            _popup.PopupPredicted(Loc.GetString("machine-insert-all", ("user", args.User), ("machine", uid), ("item", used)), uid, args.User);
    }
}

