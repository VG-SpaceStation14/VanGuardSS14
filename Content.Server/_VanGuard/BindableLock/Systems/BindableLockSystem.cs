using Content.Shared._VanGuard.BindableLock.Components;
using Content.Server.Popups;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Interaction;
using Content.Shared.PDA;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.BindableLock.Systems;

public sealed partial class BindableLockSystem : EntitySystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;

    private static readonly ProtoId<AccessLevelPrototype> PersonalLockerTag = "PersonalLocker";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BindableLockComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnInteractUsing(EntityUid uid, BindableLockComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<AccessReaderComponent>(uid, out var accessReader))
            return;

        var used = args.Used;

        StationRecordKey? key = null;
        if (TryComp<StationRecordKeyStorageComponent>(used, out var keyStorage) && keyStorage.Key != null)
        {
            key = keyStorage.Key;
        }
        else if (TryComp<PdaComponent>(used, out var pda) && pda.ContainedId is { Valid: true } id)
        {
            if (TryComp<StationRecordKeyStorageComponent>(id, out var pdaKeyStorage) && pdaKeyStorage.Key != null)
                key = pdaKeyStorage.Key;
        }

        if (component.CanBind)
        {
            if (accessReader.AccessLists.Count > 0 || accessReader.AccessKeys.Count > 0)
            {
                _popup.PopupEntity(Loc.GetString("bindable-lock-already-configured"), uid, args.User);
                return;
            }

            if (key == null)
            {
                _popup.PopupEntity(Loc.GetString("bindable-lock-no-key"), uid, args.User);
                return;
            }

            _accessReader.TrySetAccesses((uid, accessReader),
                new List<HashSet<ProtoId<AccessLevelPrototype>>> { new() { PersonalLockerTag } });

            _accessReader.AddAccessKey((uid, accessReader), key.Value);

            component.CanBind = false;
            Dirty(uid, component);

            _popup.PopupEntity(Loc.GetString("bindable-lock-bound"), uid, args.User);
            args.Handled = true;
            return;
        }

        if (key == null || !_accessReader.AreStationRecordKeysAllowed(new HashSet<StationRecordKey> { key.Value }, accessReader))
        {
            _popup.PopupEntity(Loc.GetString("bindable-lock-not-owner"), uid, args.User);
            return;
        }

        _accessReader.TryClearAccesses((uid, accessReader));
        _accessReader.ClearAccessKeys((uid, accessReader));

        component.CanBind = true;
        Dirty(uid, component);

        _popup.PopupEntity(Loc.GetString("bindable-lock-unbound"), uid, args.User);
        args.Handled = true;
    }
}