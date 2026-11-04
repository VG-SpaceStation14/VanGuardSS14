using System.Linq;
using Content.Server._VanGuard.Research.Components;
using Content.Shared._VanGuard.Research.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._VanGuard.Research.Systems;

public sealed partial class ExperimentScannerSystem
{
    /// <summary>
    /// Adds one random, not-yet-known order to the station pool.
    /// </summary>
    private bool TryAddOrder(
        EntityUid station,
        string experimentGroup,
        ExperimentStationDatabaseComponent db,
        string? excludedPrototype = null,
        Func<ResearchExperimentPrototype, bool>? filter = null)
    {
        var candidates = _proto.EnumeratePrototypes<ResearchExperimentPrototype>()
            .Where(proto => proto.Group == experimentGroup
                            && !db.UsedOrders.Contains(proto.ID)
                            && !db.AvailableOrders.Any(order => order.Prototype == proto.ID)
                            && proto.ID != excludedPrototype
                            && (filter == null || filter(proto)))
            .ToList();

        if (candidates.Count == 0)
            return false;

        // A randomly selected experiment may not be creatable on this station
        // (for example, when no matching species or vending machine exists).
        // Keep trying the remaining candidates instead of reporting the whole
        // pool as empty.
        while (candidates.Count > 0)
        {
            var selected = _random.Pick(candidates);
            candidates.Remove(selected);

            if (!TryCreateOrder(station, selected, db.NextOrderId, out var order))
                continue;

            db.NextOrderId++;
            db.AvailableOrders.Add(order);
            return true;
        }

        return false;
    }

    private bool TryCreateOrder(
        EntityUid station,
        ResearchExperimentPrototype proto,
        int ordinal,
        out StationExperimentOrderData order)
    {
        order = new StationExperimentOrderData
        {
            Id = $"EXP-{ordinal:D3}",
            Prototype = proto.ID,
            ProgressCurrent = 0,
            ProgressTarget = 1,
        };

        switch (proto.Condition)
        {
            case SpeciesReagentScanCondition species:
                if (!TryPickSpeciesOnStation(station, species, out var selectedSpecies))
                    return false;

                order.SelectedSpecies = selectedSpecies;
                order.SelectedReagent = _random.Pick(species.Reagents);
                break;

            case PrototypeMatchScanCondition prototypes:
                if (prototypes.AllowedPrototypes.Count == 0)
                    return false;
                order.SelectedPrototype = _random.Pick(prototypes.AllowedPrototypes);
                break;

            case FullyLoadedMechScanCondition mechs:
                if (mechs.AllowedPrototypes.Count == 0)
                    return false;
                order.SelectedPrototype = _random.Pick(mechs.AllowedPrototypes);
                break;

            case DelayedRescanScanCondition vending:
                if (!TryPickVendingTarget(vending, out var department, out var vendingProto))
                    return false;
                order.SelectedDepartment = department;
                order.SelectedPrototype = vendingProto;
                break;

            case TagBatchScanCondition batch:
                order.ProgressTarget = Math.Max(1, batch.RequiredCount);
                break;

            case CanisterGasScanCondition gas:
                if (gas.AllowedGases.Count == 0)
                    return false;
                order.SelectedReagent = _random.Pick(gas.AllowedGases);
                break;
        }

        return true;
    }

    private bool TryPickSpeciesOnStation(EntityUid station, SpeciesReagentScanCondition condition, out string selected)
    {
        selected = string.Empty;
        if (condition.Reagents.Count == 0)
            return false;

        var candidates = new HashSet<string>();
        var humans = EntityQueryEnumerator<HumanoidProfileComponent>();
        while (humans.MoveNext(out var uid, out var humanoid))
        {
            if (_station.GetOwningStation(uid) != station
                || !_proto.TryIndex<SpeciesPrototype>(humanoid.Species, out var species)
                || !species.RoundStart
                || condition.ExcludedSpecies.Contains(humanoid.Species))
                continue;

            candidates.Add(humanoid.Species);
        }

        if (candidates.Count == 0)
            return false;

        selected = _random.Pick(candidates.ToList());
        return true;
    }

    private bool TryPickVendingTarget(DelayedRescanScanCondition condition, out string department, out string prototype)
    {
        department = string.Empty;
        prototype = string.Empty;

        var departments = condition.DepartmentVendingPrototypes
            .Where(pair => pair.Value.Count > 0)
            .Select(pair => pair.Key)
            .ToList();

        if (departments.Count == 0)
            return false;

        department = _random.Pick(departments);
        var prototypes = condition.DepartmentVendingPrototypes[department];
        prototype = _random.Pick(prototypes);
        return true;
    }
}
