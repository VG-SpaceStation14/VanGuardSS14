using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Content.Shared._VanGuard.Research.Prototypes;

namespace Content.Server._VanGuard.Research.Components;

/// <summary>
/// Holds the pool of experiment orders that is shared between every scanner
/// linked to the same station.
/// </summary>
[RegisterComponent]
public sealed partial class ExperimentStationDatabaseComponent : Component
{
    [DataField]
    public List<StationExperimentOrderData> AvailableOrders = new();

    /// <summary>Experiment prototypes that were already completed on this station.</summary>
    [DataField]
    public HashSet<string> UsedOrders = new();

    [DataField]
    public int NextOrderId = 1;
}

/// <summary>
/// Per-scanner bookkeeping: which station it is linked to and which order it is
/// currently working on.
/// </summary>
[RegisterComponent]
public sealed partial class ExperimentScannerDatabaseComponent : Component
{
    [DataField]
    public EntityUid? LinkedStation;

    [DataField]
    public StationExperimentOrderData? ActiveOrder;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextSkipTime = TimeSpan.Zero;

    [DataField]
    public TimeSpan SkipDelay = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Runtime state of a single accepted experiment order.
/// </summary>
[DataDefinition]
public sealed partial class StationExperimentOrderData
{
    [DataField]
    public string Id = string.Empty;

    [DataField(required: true)]
    public ProtoId<ResearchExperimentPrototype> Prototype = string.Empty;

    [DataField]
    public int ProgressCurrent;

    [DataField]
    public int ProgressTarget = 1;

    /// <summary>Randomly selected target parameters for the order, if any.</summary>
    [DataField]
    public string? SelectedSpecies;

    [DataField]
    public string? SelectedReagent;

    [DataField]
    public string? SelectedPrototype;

    [DataField]
    public string? SelectedDepartment;

    /// <summary>Entity remembered for multi-step conditions (e.g. delayed rescan).</summary>
    [DataField]
    public EntityUid? SelectedEntity;

    /// <summary>When a multi-step condition allows the second scan to be performed.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan RescanAfter = TimeSpan.Zero;

    /// <summary>Distinct entities that already contributed to a batch order.</summary>
    [DataField]
    public List<EntityUid> ScannedEntities = new();

    /// <summary>
    /// Whether a research server was linked when the order was accepted.
    /// Used to prevent research disk fallback abuse after disconnecting mid-order.
    /// </summary>
    [DataField]
    public bool HadServerOnAccept;
}
