using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Research.Prototypes;

/// <summary>
/// Base class for all experiment scan conditions.
/// Each concrete condition defines what kind of entity has to be scanned
/// in order to advance (or complete) the experiment.
/// </summary>
[ImplicitDataDefinitionForInheritors, Serializable, NetSerializable]
public abstract partial class ResearchExperimentCondition;

/// <summary>
/// Requires scanning an AME controller whose fuel injection rate exceeds
/// the safe threshold multiplied by the number of active cores.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class AmeOverloadScanCondition : ResearchExperimentCondition
{
    /// <summary>Maximum safe injection amount per active core.</summary>
    [DataField]
    public int SafeInjectionPerCore = 2;

    [DataField]
    public bool RequirePowered = true;
}

/// <summary>
/// Requires scanning a living being of a randomly selected round-start species
/// that currently has one of the listed reagents in its bloodstream.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class SpeciesReagentScanCondition : ResearchExperimentCondition
{
    [DataField(required: true)]
    public List<string> Reagents = new();

    /// <summary>Solution that is checked on the target body.</summary>
    [DataField]
    public string SolutionName = "bloodstream";

    /// <summary>Species that will never be selected as the scan subject.</summary>
    [DataField]
    public List<string> ExcludedSpecies = new();
}

/// <summary>
/// Requires scanning an entity (usually a tile decal) whose solution contains a
/// specific reagent.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class SolutionReagentScanCondition : ResearchExperimentCondition
{
    [DataField(required: true)]
    public string Reagent = string.Empty;

    [DataField]
    public string SolutionName = "puddle";
}

/// <summary>
/// Requires scanning a mech whose equipment bay is completely filled.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class FullyLoadedMechScanCondition : ResearchExperimentCondition
{
    [DataField(required: true)]
    public List<string> AllowedPrototypes = new();

    /// <summary>Maps an allowed prototype to extra prototype ids that are also accepted.</summary>
    [DataField]
    public Dictionary<string, List<string>> PrototypeAliases = new();
}

/// <summary>
/// Requires scanning a single entity that matches a randomly selected prototype.
/// Used for things like pets, weapons, ID cards, seeds or materials.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class PrototypeMatchScanCondition : ResearchExperimentCondition
{
    [DataField(required: true)]
    public List<string> AllowedPrototypes = new();

    /// <summary>Maps an allowed prototype to extra prototype ids that are also accepted.</summary>
    [DataField]
    public Dictionary<string, List<string>> PrototypeAliases = new();
}

/// <summary>
/// Requires scanning the same vending machine twice, with a delay in between.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class DelayedRescanScanCondition : ResearchExperimentCondition
{
    /// <summary>Vending machines grouped by the department they belong to.</summary>
    [DataField(required: true)]
    public Dictionary<string, List<string>> DepartmentVendingPrototypes = new();

    [DataField]
    public TimeSpan RescanDelay = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Requires scanning a certain number of distinct entities that carry the given
/// tags and/or components, while avoiding entities with forbidden tags.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class TagBatchScanCondition : ResearchExperimentCondition
{
    [DataField]
    public List<string> RequiredTags = new();

    [DataField]
    public List<string> ForbiddenTags = new();

    [DataField]
    public List<string> RequiredComponents = new();

    /// <summary>How many distinct matching entities must be scanned.</summary>
    [DataField]
    public int RequiredCount = 1;
}

/// <summary>
/// Requires scanning a single entity that has all of the listed components.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class ComponentPresenceScanCondition : ResearchExperimentCondition
{
    [DataField(required: true)]
    public List<string> RequiredComponents = new();
}

/// <summary>
/// Requires scanning an entity that has the given tags/components and whose solution
/// contains at least a certain quantity of a specific reagent.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class TaggedSolutionScanCondition : ResearchExperimentCondition
{
    [DataField]
    public List<string> RequiredTags = new();

    [DataField]
    public List<string> ForbiddenTags = new();

    [DataField]
    public List<string> RequiredComponents = new();

    [DataField]
    public string SolutionName = "battery";

    [DataField(required: true)]
    public string Reagent = string.Empty;

    [DataField]
    public float Quantity = 5f;
}

/// <summary>
/// Requires scanning an entity that is currently exposed to at least a certain
/// amount of ionizing radiation.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class RadiationExposureScanCondition : ResearchExperimentCondition
{
    [DataField]
    public List<string> RequiredComponents = new();

    [DataField]
    public float MinRadiation = 6f;
}

/// <summary>
/// Requires scanning a gas canister holding at least a certain amount of a
/// randomly selected gas.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class CanisterGasScanCondition : ResearchExperimentCondition
{
    [DataField]
    public List<string> RequiredComponents = new();

    [DataField(required: true)]
    public List<string> AllowedGases = new();

    [DataField]
    public float MinMoles = 500f;
}

/// <summary>
/// Requires scanning a paper document carrying at least a certain number of
/// unique signature stamps.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class SignatureDiversityScanCondition : ResearchExperimentCondition
{
    [DataField]
    public List<string> RequiredComponents = new();

    [DataField]
    public int MinUniqueSignatures = 5;
}

/// <summary>
/// Requires scanning a machine that is in a specific powered state, optionally
/// with a functional gravity generator attached.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class PoweredStateScanCondition : ResearchExperimentCondition
{
    [DataField]
    public List<string> RequiredComponents = new();

    [DataField]
    public bool RequirePowered = true;

    [DataField]
    public bool RequireGravityActive = false;
}
