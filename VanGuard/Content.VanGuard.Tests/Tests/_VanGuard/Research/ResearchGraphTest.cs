#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Research.Prototypes;
using NUnit.Framework;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Research;

[TestFixture]
public sealed class ResearchGraphTest : GameTest
{
    private static readonly string[] ExpectedTechnologyIds =
    {
        "PowerGeneration", "PlasmaGenerator", "UraniumGenerator", "MechanicalCompression",
        "AdvancedToolsTechnology", "CompactTechnologies", "RapidPipeDispenser", "IndustrialEngineering",
        "SalvageEquipment", "MassExcavation", "AtmosphericTech", "AdvancedAtmospherics", "SpaceScanning",
        "BluespaceStorage", "Shuttlecraft", "AdvancedPowercells", "HyperCapacityBattery",
        "OptimizedMicrogalvanism", "PackagedLights", "BluespaceCargoTransport", "SalvageWeapons",
        "WeaponizedLaserManipulation", "AdvancedRiotControl", "DraconicMunitions", "NonlethalAmmunition",
        "UraniumMunitions", "ExplosiveTechnology", "SpecialMeans", "KineticModifications", "PowerCages",
        "ShuttleGuns", "AdvancedShuttleWeapon", "ConcentratedLaserWeaponry", "PortableMicrofusionWeaponry",
        "WaveParticleHarnessing", "ThermalWeaponry", "DualWieldingTechnology", "Hydroponics", "Bartending",
        "AudioVisualCommunication", "Telecommunications", "AdvancedEntertainment", "MeatManipulation",
        "FauxAstroTiles", "AdvancedCleaning", "AdvancedSpray", "PushHorn", "QuantumFiberWeaving",
        "CivilianMobility", "BasicAnomalousResearch", "AnomalyContainment", "BasicXenoArcheology",
        "AlternativeResearch", "MagnetsTech", "AnomalyCoreHarnessing", "AbnormalArtifactManipulation",
        "AdvancedAnomalyResearch", "DeterrenceTechnologies", "GravityManipulation", "QuantumLeaping",
        "BluespaceTimeManipulation", "TeleSci", "BluespaceCrystalTech", "ExperimentScannerTech",
        "ExperimentFloorScannerTech", "BasicRobotics", "CritterMechs", "RipleyAPLU", "HONKMech",
        "BiochemicalStasis", "ChemistryDispensing", "MechanizedTreatment", "LauncherSyringe",
        "Cryopreservation", "MedicalEquipment", "CloningTechnology", "BluespaceChemistry", "SmartOreBagTech",
    };

    [Test]
    public async Task TreeLoadsReachesAndIsValid()
    {
        var techs = SProtoMan.EnumeratePrototypes<TechnologyPrototype>()
            .Where(t => !t.Hidden)
            .ToList();
        var byId = techs.ToDictionary(t => t.ID);

        Assert.Multiple(() =>
        {
            Assert.That(techs, Has.Count.EqualTo(78), "all 78 technologies must load");
            Assert.That(byId.Keys, Is.EquivalentTo(ExpectedTechnologyIds), "existing technology IDs must be preserved");
            Assert.That(techs.Select(t => t.Discipline).Distinct(), Is.EquivalentTo(new[]
            {
                "Industrial", "Arsenal", "Experimental", "CivilianServices", "Mech", "Medical",
            }), "all six disciplines must remain represented");
            Assert.That(techs.Count(t => t.Discipline == "Mech"), Is.EqualTo(4),
                "Mech must remain a separate discipline with its four existing technologies");
        });

        var totalCost = techs.Sum(t => t.Cost);
        Assert.That(totalCost, Is.InRange(450_000, 550_000),
            $"tree cost {totalCost} is outside the agreed 450k-550k budget");

        var duplicateRecipes = techs
            .SelectMany(t => t.RecipeUnlocks.Select(r => (Tech: t.ID, Recipe: r)))
            .GroupBy(x => x.Recipe)
            .Where(g => g.Select(x => x.Tech).Distinct().Count() > 1)
            .ToList();
        Assert.That(duplicateRecipes, Is.Empty, "one recipe must not be granted by unrelated technologies");

        var duplicatePositions = techs
            .GroupBy(t => (t.Discipline, t.Position))
            .Where(g => g.Count() > 1)
            .ToList();
        Assert.That(duplicatePositions, Is.Empty, "technologies must not share a UI position");

        foreach (var tech in techs)
        {
            Assert.That(tech.Position.X, Is.GreaterThanOrEqualTo(0));
            Assert.That(tech.Position.Y, Is.GreaterThanOrEqualTo(0));
            Assert.That(tech.Tier, Is.InRange(1, 3));
        }

        foreach (var tech in techs)
        {
            foreach (var prereq in tech.TechnologyPrerequisites)
            {
                Assert.That(byId.ContainsKey(prereq), Is.True,
                    $"{tech.ID} references missing prerequisite {prereq}");
                Assert.That(prereq, Is.Not.EqualTo(tech.ID), $"{tech.ID} cannot require itself");
            }
        }

        var children = new Dictionary<string, List<string>>();
        foreach (var tech in techs)
            children[tech.ID] = new List<string>();
        foreach (var tech in techs)
        {
            foreach (var prereq in tech.TechnologyPrerequisites)
                children[prereq].Add(tech.ID);
        }

        var roots = techs.Where(t => t.TechnologyPrerequisites.Count == 0).Select(t => t.ID).ToList();
        Assert.That(roots, Is.Not.Empty, "the tree must have reachable roots");

        var visited = new HashSet<string>();
        var queue = new Queue<string>(roots);
        while (queue.TryDequeue(out var id))
        {
            if (!visited.Add(id))
                continue;
            foreach (var child in children[id])
                queue.Enqueue(child);
        }

        Assert.That(visited, Is.EquivalentTo(techs.Select(t => t.ID)),
            "every technology must be reachable from a root through explicit prerequisites");

        var visiting = new HashSet<string>();
        var done = new HashSet<string>();
        void Visit(string id)
        {
            if (done.Contains(id))
                return;
            Assert.That(visiting.Add(id), Is.True, $"cyclic research dependency at {id}");
            foreach (var prereq in byId[id].TechnologyPrerequisites)
                Visit(prereq);
            visiting.Remove(id);
            done.Add(id);
        }

        foreach (var tech in techs)
            Visit(tech.ID);

        var mobility = byId["CivilianMobility"];
        Assert.That(mobility.Discipline, Is.EqualTo("CivilianServices"));
        Assert.That(mobility.RecipeUnlocks, Does.Contain(new ProtoId<LatheRecipePrototype>("ClothingShoesBootsJump")));
        Assert.That(byId["SalvageEquipment"].RecipeUnlocks,
            Does.Not.Contain(new ProtoId<LatheRecipePrototype>("ClothingShoesBootsJump")));
        Assert.That(byId["QuantumFiberWeaving"].TechnologyPrerequisites, Does.Contain("CivilianMobility"));
    }
}
