using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.Radio;

namespace Content.Shared._VanGuard.Research.Components;

/// <summary>
/// Handheld experiment scanner. Lets the user accept a field experiment from the
/// station database and then scan entities with the scanner to fulfill the
/// experiment's condition.
/// </summary>
[RegisterComponent]
public sealed partial class ExperimentScannerComponent : Component
{
    /// <summary>Experiments offered by this scanner are filtered by this group.</summary>
    [DataField]
    public string ExperimentGroup = "Default";

    /// <summary>How many available orders the station database should keep filled.</summary>
    [DataField]
    public int VisibleOrders = 7;

    [DataField]
    public SoundSpecifier SelectSound = new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");

    [DataField]
    public SoundSpecifier ProgressSound = new SoundPathSpecifier("/Audio/Machines/scan_finish.ogg");

    [DataField]
    public SoundSpecifier CompleteSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    [DataField]
    public SoundSpecifier SkipSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    [DataField]
    public SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_two.ogg");

    [DataField]
    public ProtoId<RadioChannelPrototype> AnnouncementChannel = "Science";
}

[Serializable, NetSerializable]
public enum ExperimentScannerUiKey : byte
{
    Key
}

/// <summary>
/// Snapshot of a single experiment order as shown in the scanner UI.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class ExperimentOrderUiData
{
    [DataField]
    public string Id = string.Empty;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public int RewardPoints;

    [DataField]
    public int ProgressCurrent;

    [DataField]
    public int ProgressTarget = 1;

    [DataField]
    public TimeSpan? TimeRemaining;
}

[Serializable, NetSerializable]
public sealed class ExperimentScannerState : BoundUserInterfaceState
{
    public readonly List<ExperimentOrderUiData> Available;
    public readonly ExperimentOrderUiData? Active;
    public readonly TimeSpan UntilNextSkip;
    public readonly bool HasSelectedServer;
    public readonly string? SelectedServerName;

    public ExperimentScannerState(
        List<ExperimentOrderUiData> available,
        ExperimentOrderUiData? active,
        TimeSpan untilNextSkip,
        bool hasSelectedServer,
        string? selectedServerName)
    {
        Available = available;
        Active = active;
        UntilNextSkip = untilNextSkip;
        HasSelectedServer = hasSelectedServer;
        SelectedServerName = selectedServerName;
    }
}

[Serializable, NetSerializable]
public sealed class ExperimentSelectOrderMessage : BoundUserInterfaceMessage
{
    public readonly string Id;

    public ExperimentSelectOrderMessage(string id)
    {
        Id = id;
    }
}

[Serializable, NetSerializable]
public sealed class ExperimentAbandonOrderMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class ExperimentSkipOrderMessage : BoundUserInterfaceMessage
{
    public readonly string Id;

    public ExperimentSkipOrderMessage(string id)
    {
        Id = id;
    }
}
