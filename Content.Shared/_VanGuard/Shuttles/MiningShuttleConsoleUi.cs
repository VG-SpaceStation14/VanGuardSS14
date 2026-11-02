using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Shuttles;

[Serializable, NetSerializable]
public enum MiningShuttleConsoleUiKey : byte
{
    Key,
}

/// <summary>
/// One dock the mining shuttle console can travel to.
/// </summary>
[Serializable, NetSerializable]
public sealed class MiningShuttleDockEntry
{
    public NetEntity Dock;
    public string Name;
    public bool Enabled;
    public string? Reason;

    public MiningShuttleDockEntry(NetEntity dock, string name, bool enabled, string? reason)
    {
        Dock = dock;
        Name = name;
        Enabled = enabled;
        Reason = reason;
    }
}

/// <summary>
/// State of the mining shuttle console. The list is rebuilt whenever the UI opens
/// or a trip is requested.
/// </summary>
[Serializable, NetSerializable]
public sealed class MiningShuttleConsoleState : BoundUserInterfaceState
{
    public List<MiningShuttleDockEntry> Docks;
    public string? Error;

    public MiningShuttleConsoleState(List<MiningShuttleDockEntry> docks, string? error)
    {
        Docks = docks;
        Error = error;
    }
}

/// <summary>
/// Requests travel to a specific tagged dock.
/// </summary>
[Serializable, NetSerializable]
public sealed class MiningShuttleConsoleTravelMessage : BoundUserInterfaceMessage
{
    public NetEntity Dock;

    public MiningShuttleConsoleTravelMessage(NetEntity dock)
    {
        Dock = dock;
    }
}
