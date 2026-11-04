using Content.Shared.Shuttles.Systems;
using Content.Shared.Timing;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Shuttles;

[Serializable, NetSerializable]
public enum MiningShuttleConsoleUiKey : byte
{
    MiningShuttleConsole = 1,
}

[Serializable, NetSerializable]
public enum GuestShuttleConsoleUiKey : byte
{
    GuestShuttleConsole = 1,
}

[Serializable, NetSerializable]
public sealed class MiningShuttleDestination
{
    public string Name;
    public bool Available;
    public string? Reason;

    public MiningShuttleDestination(string name, bool available, string? reason)
    {
        Name = name;
        Available = available;
        Reason = reason;
    }
}

[Serializable, NetSerializable]
public sealed class MiningShuttleConsoleState : BoundUserInterfaceState
{
    public string Title;
    public FTLState FTLState;
    public StartEndTime FTLTime;
    public bool HasShuttle;
    public List<MiningShuttleDestination> Destinations;
    public string? Error;

    public MiningShuttleConsoleState(
        string title,
        FTLState ftlState,
        StartEndTime ftlTime,
        bool hasShuttle,
        List<MiningShuttleDestination> destinations,
        string? error)
    {
        Title = title;
        FTLState = ftlState;
        FTLTime = ftlTime;
        HasShuttle = hasShuttle;
        Destinations = destinations;
        Error = error;
    }
}

[Serializable, NetSerializable]
public sealed class MiningShuttleConsoleTravelMessage : BoundUserInterfaceMessage
{
    public int Index;

    public MiningShuttleConsoleTravelMessage(int index)
    {
        Index = index;
    }
}
