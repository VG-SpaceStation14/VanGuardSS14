using Content.Shared.Doors.Components;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Shuttles;

[Serializable, NetSerializable]
public enum GuestShutterConsoleUiKey : byte
{
    GuestShutterConsole = 1,
}

[Serializable, NetSerializable]
public sealed class GuestShutterEntry
{
    public NetEntity Door;
    public string Name;
    public DoorState State;

    public GuestShutterEntry(NetEntity door, string name, DoorState state)
    {
        Door = door;
        Name = name;
        State = state;
    }
}

[Serializable, NetSerializable]
public sealed class GuestShutterConsoleState : BoundUserInterfaceState
{
    public List<GuestShutterEntry> Shutters;
    public string? Error;

    public GuestShutterConsoleState(List<GuestShutterEntry> shutters, string? error)
    {
        Shutters = shutters;
        Error = error;
    }
}

[Serializable, NetSerializable]
public sealed class GuestShutterToggleMessage : BoundUserInterfaceMessage
{
    public NetEntity Door;

    public GuestShutterToggleMessage(NetEntity door)
    {
        Door = door;
    }
}
