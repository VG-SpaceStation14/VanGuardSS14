using System.Linq;
using Content.Server.Ame;
using Content.Server.Ame.Components;
using Content.Server._VanGuard.Research.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Radiation.Components;
using Content.Shared._VanGuard.Research.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Mech.Components;
using Content.Shared.NodeContainer;
using Content.Shared.Paper;
using Content.Shared.Research.Components;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Research.Systems;

public sealed partial class ExperimentScannerSystem
{
    /// <summary>
    /// Evaluates the active order's condition against one scanned entity and advances the order
    /// only when the entity satisfies the condition.
    /// </summary>
    private bool AdvanceOrder(EntityUid station, StationExperimentOrderData order, EntityUid target)
    {
        var condition = _proto.Index(order.Prototype).Condition;

        switch (condition)
        {
            case AmeOverloadScanCondition ame:
                return CheckAme(ame, order, target);

            case SpeciesReagentScanCondition species:
                return CheckSpecies(species, order, target);

            case SolutionReagentScanCondition solution:
                return CheckSolution(solution, order, target);

            case FullyLoadedMechScanCondition mech:
                return CheckMech(mech, order, target);

            case PrototypeMatchScanCondition prototype:
                return CheckPrototype(prototype, order, target);

            case DelayedRescanScanCondition vending:
                return CheckDelayedRescan(vending, order, target);

            case TagBatchScanCondition batch:
                return CheckBatch(batch, order, target);

            case ComponentPresenceScanCondition components:
                return CheckComponents(components.RequiredComponents, order, target);

            case TaggedSolutionScanCondition tagged:
                return CheckTaggedSolution(tagged, order, target);

            case RadiationExposureScanCondition radiation:
                return CheckRadiation(radiation, order, target);

            case CanisterGasScanCondition gas:
                return CheckCanister(gas, order, target);

            case SignatureDiversityScanCondition signatures:
                return CheckSignatures(signatures, order, target);

            case PoweredStateScanCondition powered:
                return CheckPowered(powered, order, target);

            default:
                return false;
        }
    }

    private bool CheckAme(AmeOverloadScanCondition condition, StationExperimentOrderData order, EntityUid target)
    {
        if (!TryComp<AmeControllerComponent>(target, out var controller)
            || !TryGetAmeCores(target, out var cores)
            || cores <= 0
            || condition.RequirePowered && !this.IsPowered(target, EntityManager))
            return false;

        if (controller.InjectionAmount <= condition.SafeInjectionPerCore * cores)
            return false;

        order.ProgressCurrent = 1;
        return true;
    }

    private bool CheckSpecies(SpeciesReagentScanCondition condition, StationExperimentOrderData order, EntityUid target)
    {
        if (order.SelectedSpecies == null
            || order.SelectedReagent == null
            || !TryComp<HumanoidProfileComponent>(target, out var profile)
            || profile.Species != order.SelectedSpecies
            || !TryGetSolution(target, condition.SolutionName, out var solution))
            return false;

        if (!solution.Contents.Any(reagent => reagent.Reagent.Prototype == order.SelectedReagent))
            return false;

        order.ProgressCurrent = 1;
        return true;
    }

    private bool CheckSolution(SolutionReagentScanCondition condition, StationExperimentOrderData order, EntityUid target)
    {
        if (!TryGetSolution(target, condition.SolutionName, out var solution)
            || !solution.Contents.Any(reagent => reagent.Reagent.Prototype == condition.Reagent))
            return false;

        order.ProgressCurrent = 1;
        return true;
    }

    private bool CheckMech(FullyLoadedMechScanCondition condition, StationExperimentOrderData order, EntityUid target)
    {
        var meta = MetaData(target);
        if (!TryComp<MechComponent>(target, out var mech) || meta.EntityPrototype == null)
            return false;

        if (order.SelectedPrototype is { } selected)
        {
            var exact = meta.EntityPrototype.ID == selected;
            var alias = condition.PrototypeAliases.TryGetValue(selected, out var aliases)
                        && aliases.Contains(meta.EntityPrototype.ID);
            if (!exact && !alias)
                return false;
        }
        else if (!condition.AllowedPrototypes.Contains(meta.EntityPrototype.ID))
        {
            return false;
        }

        if (mech.EquipmentContainer.ContainedEntities.Count < mech.MaxEquipmentAmount)
            return false;

        order.ProgressCurrent = 1;
        return true;
    }

    private bool CheckPrototype(PrototypeMatchScanCondition condition, StationExperimentOrderData order, EntityUid target)
    {
        var meta = MetaData(target);
        if (order.SelectedPrototype == null || meta.EntityPrototype?.ID is not { } scanned)
            return false;

        var exact = scanned == order.SelectedPrototype;
        var alias = condition.PrototypeAliases.TryGetValue(order.SelectedPrototype, out var aliases)
                    && aliases.Contains(scanned);
        if (!exact && !alias)
            return false;

        order.ProgressCurrent = 1;
        return true;
    }

    private bool CheckDelayedRescan(DelayedRescanScanCondition condition, StationExperimentOrderData order, EntityUid target)
    {
        var meta = MetaData(target);
        if (order.SelectedPrototype == null || meta.EntityPrototype?.ID != order.SelectedPrototype)
            return false;

        if (order.ProgressCurrent == 0)
        {
            order.ProgressCurrent = 1;
            order.SelectedEntity = target;
            order.RescanAfter = _timing.CurTime + condition.RescanDelay;
            return true;
        }

        if (order.SelectedEntity != target || _timing.CurTime < order.RescanAfter)
            return false;

        order.ProgressCurrent = 2;
        return true;
    }

    private bool CheckBatch(TagBatchScanCondition condition, StationExperimentOrderData order, EntityUid target)
    {
        if (!HasTags(target, condition.RequiredTags, condition.ForbiddenTags)
            || condition.RequiredComponents.Count > 0 && !HasComponents(target, condition.RequiredComponents)
            || order.ScannedEntities.Contains(target))
            return false;

        order.ScannedEntities.Add(target);
        if (order.ProgressCurrent < order.ProgressTarget)
            order.ProgressCurrent++;

        return true;
    }

    private bool CheckComponents(List<string> required, StationExperimentOrderData order, EntityUid target)
    {
        if (!HasComponents(target, required))
            return false;

        order.ProgressCurrent = 1;
        return true;
    }

    private bool CheckTaggedSolution(TaggedSolutionScanCondition condition, StationExperimentOrderData order, EntityUid target)
    {
        if (!HasTags(target, condition.RequiredTags, condition.ForbiddenTags)
            || condition.RequiredComponents.Count > 0 && !HasComponents(target, condition.RequiredComponents)
            || !TryGetSolution(target, condition.SolutionName, out var solution))
            return false;

        var amount = solution.Contents
            .Where(reagent => reagent.Reagent.Prototype == condition.Reagent)
            .Sum(reagent => reagent.Quantity.Float());

        if (amount < condition.Quantity)
            return false;

        order.ProgressCurrent = 1;
        return true;
    }

    private bool CheckRadiation(RadiationExposureScanCondition condition, StationExperimentOrderData order, EntityUid target)
    {
        if (!HasComponents(target, condition.RequiredComponents)
            || !TryComp<RadiationReceiverComponent>(target, out var receiver)
            || receiver.CurrentRadiation < condition.MinRadiation)
            return false;

        order.ProgressCurrent = 1;
        return true;
    }

    private bool CheckCanister(CanisterGasScanCondition condition, StationExperimentOrderData order, EntityUid target)
    {
        if (order.SelectedReagent == null
            || !HasComponents(target, condition.RequiredComponents)
            || !TryComp<GasCanisterComponent>(target, out var canister)
            || !Enum.TryParse<Gas>(order.SelectedReagent, true, out var gas)
            || canister.Air.GetMoles(gas) < condition.MinMoles)
            return false;

        order.ProgressCurrent = 1;
        return true;
    }

    private bool CheckSignatures(SignatureDiversityScanCondition condition, StationExperimentOrderData order, EntityUid target)
    {
        if (!HasComponents(target, condition.RequiredComponents)
            || !TryComp<PaperComponent>(target, out var paper))
            return false;

        var unique = paper.StampedBy
            .Select(stamp => stamp.StampedName.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (unique < condition.MinUniqueSignatures)
            return false;

        order.ProgressCurrent = 1;
        return true;
    }

    private bool CheckPowered(PoweredStateScanCondition condition, StationExperimentOrderData order, EntityUid target)
    {
        if (!HasComponents(target, condition.RequiredComponents))
            return false;

        if (condition.RequirePowered && !this.IsPowered(target, EntityManager))
            return false;

        if (condition.RequireGravityActive
            && (!TryComp<GravityGeneratorComponent>(target, out var gravity) || !gravity.GravityActive))
            return false;

        order.ProgressCurrent = 1;
        return true;
    }

    private bool TryGetSolution(EntityUid target, string name, out Solution solution)
    {
        if (!_solution.TryGetSolution(target, name, out _, out var found) || found == null)
        {
            solution = default!;
            return false;
        }

        solution = found;
        return true;
    }

    private bool HasTags(EntityUid target, List<string> required, List<string> forbidden)
    {
        foreach (var tag in required)
        {
            if (!_tag.HasTag(target, tag))
                return false;
        }

        foreach (var tag in forbidden)
        {
            if (_tag.HasTag(target, tag))
                return false;
        }

        return true;
    }

    private bool HasComponents(EntityUid target, List<string> required)
    {
        if (required.Count == 0)
            return false;

        foreach (var name in required)
        {
            if (!EntityManager.ComponentFactory.TryGetRegistration(name, out var registration, true))
                return false;

            if (!EntityManager.GetEntityQuery(registration.Type).HasComp(target))
                return false;
        }

        return true;
    }

    private bool TryGetAmeCores(EntityUid controller, out int cores)
    {
        cores = 0;
        if (!TryComp<NodeContainerComponent>(controller, out var nodes))
            return false;

        var group = nodes.Nodes.Values
            .Select(node => node.NodeGroup)
            .OfType<AmeNodeGroup>()
            .FirstOrDefault();

        if (group == null)
            return false;

        cores = group.CoreCount;
        return true;
    }
}
