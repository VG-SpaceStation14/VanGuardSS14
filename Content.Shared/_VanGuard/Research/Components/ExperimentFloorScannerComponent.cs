using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.Radio;

namespace Content.Shared._VanGuard.Research.Components;

/// <summary>
/// Floor-mounted experiment scanner. Accepts the same orders as the handheld
/// scanner, but instead of scanning a single target it sweeps every loose item
/// standing on its tile.
/// </summary>
[RegisterComponent]
public sealed partial class ExperimentFloorScannerComponent : Component
{
    public const string ContainerId = "experiment-floor-scanner-container";

    /// <summary>Experiments offered by this scanner are filtered by this group.</summary>
    [DataField]
    public string ExperimentGroup = "Default";

    /// <summary>How many available orders the station database should keep filled.</summary>
    [DataField]
    public int VisibleOrders = 7;

    /// <summary>Time between starting the scan and the visual state switching to scanning.</summary>
    [DataField]
    public TimeSpan ScanDuration = TimeSpan.FromSeconds(3.0);

    /// <summary>Delay between processing individual items in a batch.</summary>
    [DataField]
    public TimeSpan ItemProcessDelay = TimeSpan.FromSeconds(0.3);

    /// <summary>How long items are held inside the machine before being ejected.</summary>
    [DataField]
    public TimeSpan CapsuleStepDuration = TimeSpan.FromSeconds(0.8);

    public bool IsProcessing;

    [DataField]
    public SoundSpecifier SuccessSound = new SoundPathSpecifier("/Audio/Machines/scan_finish.ogg");

    [DataField]
    public SoundSpecifier FailureSound = new SoundPathSpecifier("/Audio/Machines/buzz-two.ogg");

    [DataField]
    public SoundSpecifier ProgressSound = new SoundPathSpecifier("/Audio/Machines/scan_finish.ogg");

    [DataField]
    public SoundSpecifier CompleteSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    [DataField]
    public ProtoId<RadioChannelPrototype> AnnouncementChannel = "Science";

    [DataField]
    public AudioParams AudioParams = AudioParams.Default.WithVolume(-8f).WithVariation(0.25f);
}

/// <summary>Visual states of the floor scanner machine.</summary>
[Serializable, NetSerializable]
public enum ExperimentFloorScannerVisualState : byte
{
    /// <summary>Ready, waiting for a scan to be started.</summary>
    Idle,

    /// <summary>Arm raised, items have been processed and are being ejected.</summary>
    Up,

    /// <summary>Arm lowered, a scan is about to start.</summary>
    Down,

    /// <summary>Items are currently being scanned.</summary>
    Scanning
}

[Serializable, NetSerializable]
public enum ExperimentFloorScannerVisualLayers : byte
{
    Base
}

[Serializable, NetSerializable]
public enum ExperimentFloorScannerVisuals : byte
{
    State
}

[Serializable, NetSerializable]
public enum ExperimentFloorScannerUiKey : byte
{
    Key,
}

/// <summary>Sent by the client to start scanning the items on the scanner's tile.</summary>
[Serializable, NetSerializable]
public sealed class ExperimentFloorScannerPerformMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class ExperimentFloorScannerState : BoundUserInterfaceState
{
    public readonly List<ExperimentOrderUiData> Available;
    public readonly ExperimentOrderUiData? Active;
    public readonly TimeSpan UntilNextSkip;
    public readonly bool HasSelectedServer;
    public readonly string? SelectedServerName;
    public readonly bool IsProcessing;

    public ExperimentFloorScannerState(
        List<ExperimentOrderUiData> available,
        ExperimentOrderUiData? active,
        TimeSpan untilNextSkip,
        bool hasSelectedServer,
        string? selectedServerName,
        bool isProcessing)
    {
        Available = available;
        Active = active;
        UntilNextSkip = untilNextSkip;
        HasSelectedServer = hasSelectedServer;
        SelectedServerName = selectedServerName;
        IsProcessing = isProcessing;
    }
}
