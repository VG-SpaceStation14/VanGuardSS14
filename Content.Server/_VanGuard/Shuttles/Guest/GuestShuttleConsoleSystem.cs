using Content.Server.Shuttles.Systems;
using Content.Shared._VanGuard.Shuttles;
using Content.Shared.Access.Systems;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.Server._VanGuard.Shuttles.Guest;

/// <summary>
/// Local shutter/airlock control for the guest shuttle. This console does not
/// choose FTL destinations and has no dependency on Lavaland.
/// </summary>
public sealed partial class GuestShuttleConsoleSystem : EntitySystem
{
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private SharedDoorSystem _door = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GuestShuttleConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<GuestShuttleShutterComponent, DoorStateChangedEvent>(OnDoorStateChanged);
        SubscribeLocalEvent<GuestShuttleShutterComponent, ComponentShutdown>(OnShutterShutdown);

        Subs.BuiEvents<GuestShuttleConsoleComponent>(
            GuestShutterConsoleUiKey.GuestShutterConsole,
            subs =>
            {
                subs.Event<GuestShutterToggleMessage>(OnToggle);
            });
    }

    private void OnOpened(Entity<GuestShuttleConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not GuestShutterConsoleUiKey.GuestShutterConsole)
            return;

        UpdateUi(ent);
    }

    private void OnToggle(Entity<GuestShuttleConsoleComponent> ent, ref GuestShutterToggleMessage args)
    {
        if (!TryGetEntity(args.Door, out var doorUid))
        {
            UpdateUi(ent, Loc.GetString("guest-shutter-invalid"));
            return;
        }

        TryToggle(ent, doorUid.Value, args.Actor, out var error);
        UpdateUi(ent, error);
    }

    public bool TryToggle(
        Entity<GuestShuttleConsoleComponent> console,
        EntityUid doorUid,
        EntityUid actor,
        out string? error)
    {
        error = null;

        if (!HasComp<GuestShuttleShutterComponent>(doorUid) ||
            !TryComp<DoorComponent>(doorUid, out var door))
        {
            error = Loc.GetString("guest-shutter-invalid");
            return false;
        }

        if (!_access.IsAllowed(actor, console.Owner))
        {
            error = Loc.GetString("mining-console-access-denied");
            return false;
        }

        if (!TryGetShuttle(console, out var shuttle) ||
            Transform(doorUid).GridUid != shuttle)
        {
            error = Loc.GetString("guest-shutter-invalid");
            return false;
        }

        if (!_door.TryToggleDoor(doorUid, door, actor))
        {
            error = Loc.GetString("guest-shutter-failed");
            return false;
        }

        return true;
    }

    private void OnDoorStateChanged(Entity<GuestShuttleShutterComponent> ent, ref DoorStateChangedEvent args)
    {
        var grid = Transform(ent.Owner).GridUid;
        if (grid is { } gridUid)
            UpdateConsolesUsing(gridUid);
    }

    private void OnShutterShutdown(Entity<GuestShuttleShutterComponent> ent, ref ComponentShutdown args)
    {
        var grid = Transform(ent.Owner).GridUid;
        if (grid is { } gridUid)
            UpdateConsolesUsing(gridUid);
    }

    public bool TryGetShuttle(Entity<GuestShuttleConsoleComponent> console, out EntityUid shuttle)
    {
        shuttle = default;

        if (console.Comp.LinkedShuttle is { } linked)
        {
            if (!Exists(linked) || !HasComp<ShuttleComponent>(linked))
                return false;

            shuttle = linked;
            return true;
        }

        var grid = Transform(console.Owner).GridUid;
        if (grid is { } current && HasComp<GuestShuttleComponent>(current))
        {
            shuttle = current;
            return true;
        }

        EntityUid? found = null;
        var query = AllEntityQuery<GuestShuttleComponent>();
        while (query.MoveNext(out var candidate, out _))
        {
            if (!HasComp<ShuttleComponent>(candidate))
                continue;

            if (found != null)
                return false;

            found = candidate;
        }

        if (found is { } result)
        {
            shuttle = result;
            return true;
        }

        return false;
    }

    private void UpdateConsolesUsing(EntityUid shuttle)
    {
        var consoles = AllEntityQuery<GuestShuttleConsoleComponent>();
        while (consoles.MoveNext(out var uid, out var component))
        {
            var console = (uid, component);
            if (TryGetShuttle(console, out var controlled) && controlled == shuttle)
                UpdateUi(console);
        }
    }

    private void UpdateUi(Entity<GuestShuttleConsoleComponent> console, string? error = null)
    {
        var entries = new List<GuestShutterEntry>();
        if (TryGetShuttle(console, out var shuttle))
        {
            var query = AllEntityQuery<GuestShuttleShutterComponent, DoorComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var door, out var xform))
            {
                if (xform.GridUid != shuttle)
                    continue;

                var name = MetaData(uid).EntityName;
                if (string.IsNullOrWhiteSpace(name))
                    name = Loc.GetString("guest-shutter-unnamed");

                entries.Add(new GuestShutterEntry(
                    GetNetEntity(uid),
                    name,
                    door.State));
            }
        }
        else
        {
            error ??= Loc.GetString("mining-console-no-shuttle");
        }

        entries.Sort((a, b) => a.Door.Id.CompareTo(b.Door.Id));
        _ui.SetUiState(
            console.Owner,
            GuestShutterConsoleUiKey.GuestShutterConsole,
            new GuestShutterConsoleState(entries, error));
    }
}
