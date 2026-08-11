using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.Research.Prototypes;

/// <summary>
/// Defines a single field experiment that can be issued through an experiment scanner.
/// Completing an experiment grants research points to the connected research server
/// (or spawns a research disk as a fallback when no server is linked).
/// </summary>
[Prototype]
public sealed partial class ResearchExperimentPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Localization key for the experiment title shown in the scanner UI.</summary>
    [DataField(required: true)]
    public LocId Name = string.Empty;

    /// <summary>Localization key for the experiment description shown in the scanner UI.</summary>
    [DataField(required: true)]
    public LocId Description = string.Empty;

    /// <summary>Amount of research points awarded when the experiment is completed.</summary>
    [DataField]
    public int RewardPoints = 1000;

    /// <summary>
    /// Orders are grouped. A scanner only offers orders that share its group,
    /// which allows different machines to offer different experiment pools.
    /// </summary>
    [DataField]
    public string Group = "Default";

    /// <summary>The condition that must be satisfied by scanning entities.</summary>
    [DataField(required: true)]
    public ResearchExperimentCondition Condition = default!;
}
