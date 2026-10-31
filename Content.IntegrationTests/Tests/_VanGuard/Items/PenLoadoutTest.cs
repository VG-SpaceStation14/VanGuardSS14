using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._VanGuard.Paper;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Station.Systems;
using Robust.Shared.GameObjects.Components;
using Robust.Shared.Enums;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Items;

/// <summary>
///     Checks the pens ported from the old VanGuard build and their selection in the PDA loadout.
/// </summary>
[TestFixture]
public sealed class PenLoadoutTest : GameTest
{
    private static readonly ProtoId<SpeciesPrototype> HumanSpecies = "Human";

    /// <summary>
    ///     Ported pens and the ink colour their Signature component must have.
    /// </summary>
    private static readonly Dictionary<string, SignatureInkColor> PortedPens = new()
    {
        { "PenAtmos", SignatureInkColor.Blue },
        { "PenBooze", SignatureInkColor.Brown },
        { "PenCargo", SignatureInkColor.Orange },
        { "PenChaplain", SignatureInkColor.Black },
        { "PenChemist", SignatureInkColor.Orange },
        { "PenEngineering", SignatureInkColor.Yellow },
        { "PenGreen", SignatureInkColor.Green },
        { "PenHearts", SignatureInkColor.Pink },
        { "PenHydroponics", SignatureInkColor.Green },
        { "PenJanitor", SignatureInkColor.Purple },
        { "PenLightBlue", SignatureInkColor.LightBlue },
        { "PenMedical1", SignatureInkColor.Blue },
        { "PenMedical2", SignatureInkColor.Blue },
        { "PenCamouflage", SignatureInkColor.Green },
        { "PenMime", SignatureInkColor.Gray },
        { "PenPink", SignatureInkColor.Pink },
        { "PenPurple", SignatureInkColor.Purple },
        { "PenRainbow", SignatureInkColor.Purple },
        { "PenMedia", SignatureInkColor.Blue },
        { "PenYellow", SignatureInkColor.Yellow },
        { "PenViolet", SignatureInkColor.Purple },
        { "PenStarry", SignatureInkColor.Purple },
        { "PenScienceFountain", SignatureInkColor.LightBlue },
        { "PenScienceOverpriced", SignatureInkColor.Blue },
        { "PenScience", SignatureInkColor.Blue },
        { "PenBrigmedic", SignatureInkColor.Red },
        { "PenSecurityBlack", SignatureInkColor.Black },
        { "PenSecurityRed", SignatureInkColor.Red },
        { "PenSecurityFountain", SignatureInkColor.Red },
        { "PenSecurityFountainMetallic", SignatureInkColor.Red },
        { "BlackFountainPen", SignatureInkColor.Black },
        { "PenMedicalCMO", SignatureInkColor.LightBlue },
        { "PenEngineeringCE", SignatureInkColor.Yellow },
        { "PenCargo1QM", SignatureInkColor.Gold },
    };

    /// <summary>
    ///     Department heads keep the pen of their own PDA and must not get the generic pen selection.
    /// </summary>
    private static readonly Dictionary<string, string> HeadPens = new()
    {
        { "Captain", "PenCap" },
        { "HeadOfPersonnel", "PenHop" },
        { "HeadOfSecurity", "PenSecurityFountainMetallic" },
        { "ChiefEngineer", "PenEngineeringCE" },
        { "ChiefMedicalOfficer", "PenMedicalCMO" },
        { "ResearchDirector", "PenScienceFountain" },
        { "Quartermaster", "PenCargo1QM" },
    };

    private const string PenGroup = "PdaPen";

    private static IPrototypeManager ProtoMan => IoCManager.Instance!.Resolve<IPrototypeManager>();

    private static HumanoidCharacterProfile CreateProfile() =>
        HumanoidCharacterProfile.DefaultWithSpecies(HumanSpecies, Sex.Male);

    private static IPrototypeManager Proto => ProtoMan;

    private static List<EntityPrototype> Pens()
    {
        var factory = IoCManager.Instance!.Resolve<IComponentFactory>();
        return Proto.EnumeratePrototypes<EntityPrototype>()
            .Where(p => p.TryComp<SignatureComponent>(out _, factory))
            .ToList();
    }

    private HumanoidCharacterProfile CreateProfileWithPen(string job, string pen)
    {
        var profile = CreateProfile();
        var roleLoadout = new RoleLoadout($"Job{job}");
        roleLoadout.SetDefault(profile, ServerSession, Proto, force: true);

        Assert.That(roleLoadout.AddLoadout(PenGroup, pen, Proto), Is.True,
            $"{pen} should be selectable for {job}");

        return profile.WithLoadout(roleLoadout);
    }

    private (EntityUid Mob, EntityUid? Pda) SpawnWithJob(EntityCoordinates coords, string job, HumanoidCharacterProfile? profile = null)
    {
        var mob = SEntMan.System<StationSpawningSystem>().SpawnPlayerMob(coords, job, profile ?? CreateProfile(), null);

        var inventory = SEntMan.System<InventorySystem>();
        inventory.TryGetSlotEntity(mob, "id", out var pda);
        return (mob, pda);
    }

    private string? GetPen(EntityUid pda)
    {
        var penSlot = SEntMan.GetComponent<PdaComponent>(pda).PenSlot;
        if (penSlot.Item is not { } pen)
            return null;

        return SEntMan.GetComponent<MetaDataComponent>(pen).EntityPrototype?.ID;
    }

    private bool HasPenGroup(string job)
    {
        if (!Proto.TryIndex<RoleLoadoutPrototype>($"Job{job}", out var role))
            return false;

        return role.Groups.Contains(PenGroup);
    }

    [Test]
    public async Task AllPortedPensUseTheInkSystem()
    {
        await Server.WaitAssertion(() =>
        {
            var factory = SEntMan.ComponentFactory;

            foreach (var (id, expectedColour) in PortedPens)
            {
                Assert.That(Proto.TryIndex<EntityPrototype>(id, out var pen), Is.True, $"Missing pen prototype {id}");
                Assert.That(pen!.TryComp<SignatureComponent>(out var signature, factory), Is.True,
                    $"{id} should use the Signature (ink) component");
                Assert.That(signature!.Color, Is.EqualTo(expectedColour), $"{id} should use {expectedColour} ink");
                Assert.That(IoCManager.Instance!.Resolve<ILocalizationManager>().HasString($"ent-{id}"), Is.True,
                    $"Missing localization for {id}");
            }

            Assert.That(Pens().Count, Is.GreaterThanOrEqualTo(PortedPens.Count));
        });
    }

    [Test]
    public async Task PenGroupOffersTwoToFivePensPerInkColour()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.That(Proto.TryIndex<LoadoutGroupPrototype>(PenGroup, out var group), Is.True);
            Assert.That(group!.MinLimit, Is.EqualTo(0), "Pens should be optional");
            Assert.That(group.MaxLimit, Is.EqualTo(1), "Only one pen can be selected");

            var loadouts = group.Loadouts.Select(id => Proto.Index(id)).ToList();
            Assert.That(loadouts.Count, Is.InRange(2, 1000));

            var byColour = loadouts.GroupBy(l => l.GroupBy ?? string.Empty).ToList();
            Assert.That(byColour.Count, Is.GreaterThanOrEqualTo(4), "Pens should be grouped by ink colour");

            foreach (var colour in byColour)
            {
                Assert.That(colour.Key, Is.Not.Empty, "Every pen loadout must have a colour subgroup");
                Assert.That(colour.Count(), Is.InRange(2, 5),
                    $"Colour {colour.Key} should offer between 2 and 5 visually different pens");

                var states = new HashSet<string>();
                foreach (var loadout in colour)
                {
                    Assert.That(loadout.Storage.TryGetValue("id", out var items), Is.True,
                        $"{loadout.ID} should put the pen into the PDA pen slot");
                    Assert.That(items, Has.Count.EqualTo(1));
                    Assert.That(loadout.Equipment, Is.Empty);

                    var pen = Proto.Index(items![0]);
                    Assert.That(states.Add(pen.ID), Is.True, $"Duplicate pen {pen.ID} in colour {colour.Key}");

                    Assert.That(pen.TryComp<SignatureComponent>(out var ink, SEntMan.ComponentFactory), Is.True);
                    Assert.That(ink!.Color.ToString().ToLowerInvariant(), Is.EqualTo(colour.Key["pen-".Length..]),
                        $"{pen.ID} should write with {colour.Key["pen-".Length..]} ink");
                }
            }
        });
    }

    [Test]
    public async Task CrewJobsHavePenSelectionAndHeadsDoNot()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var job in HeadPens.Keys)
            {
                Assert.That(HasPenGroup(job), Is.False,
                    $"{job} has its own unique pen and must not get the generic pen selection");
            }

            var crew = new[]
            {
                "Passenger", "Bartender", "Chef", "Chemist", "Clown", "Detective", "Janitor", "Librarian",
                "MedicalDoctor", "MedicalIntern", "Mime", "Musician", "Paramedic", "Psychologist", "Reporter",
                "ResearchAssistant", "SalvageSpecialist", "Scientist", "SecurityCadet", "SecurityOfficer",
                "ServiceWorker", "StationEngineer", "TechnicalAssistant", "TramDriver", "Warden", "Lawyer",
                "CargoTechnician", "AtmosphericTechnician", "Brigmedic", "Botanist", "Chaplain", "Quartermaster",
            };

            foreach (var job in crew.Where(j => j != "Quartermaster"))
            {
                Assert.That(HasPenGroup(job), Is.True, $"{job} should be able to select a pen");
            }

            // Quartermaster is a head: the pen is defined by the PDA prototype.
            Assert.That(HasPenGroup("Quartermaster"), Is.False);
        });
    }

    [Test]
    public async Task SelectedPenReplacesTheDefaultPdaPen()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            // Bartender already gets its PDA from a loadout group.
            var (_, defaultPda) = SpawnWithJob(new EntityCoordinates(map.Grid, 0, 0), "Bartender");
            Assert.That(defaultPda, Is.Not.Null);
            Assert.That(GetPen(defaultPda!.Value), Is.EqualTo("Pen"),
                "Without a selection the PDA should keep its default pen");

            var profile = CreateProfileWithPen("Bartender", "PenBrigmedic");
            var (_, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, 2, 0), "Bartender", profile);

            Assert.That(pda, Is.Not.Null);
            Assert.That(GetPen(pda!.Value), Is.EqualTo("PenBrigmedic"),
                "The selected pen should be inserted into the PDA pen slot");
        });
    }

    [Test]
    public async Task PenSelectionWorksForMigratedPdaJobs()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            // Chemist used to get its PDA from the starting gear, the pen selection must work there too.
            var (_, defaultPda) = SpawnWithJob(new EntityCoordinates(map.Grid, 0, 0), "Chemist");
            Assert.That(defaultPda, Is.Not.Null, "Chemist should still get a PDA");
            Assert.That(GetPen(defaultPda!.Value), Is.EqualTo("Pen"));

            var profile = CreateProfileWithPen("Chemist", "PenChemist");
            var (_, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, 2, 0), "Chemist", profile);

            Assert.That(pda, Is.Not.Null);
            Assert.That(GetPen(pda!.Value), Is.EqualTo("PenChemist"));
        });
    }

    [Test]
    public async Task EveryJobWithPenSelectionReceivesTheChosenPen()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var jobs = Proto.EnumeratePrototypes<RoleLoadoutPrototype>()
                .Where(r => r.ID.StartsWith("Job") && r.Groups.Contains(PenGroup))
                .Select(r => r.ID[3..])
                .OrderBy(j => j)
                .ToList();

            Assert.That(jobs.Count, Is.GreaterThanOrEqualTo(30),
                "Most crew jobs should offer the pen selection");

            var x = 0;
            foreach (var job in jobs)
            {
                var profile = CreateProfileWithPen(job, "PenGreen");
                var (_, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, x, 0), job, profile);
                x += 2;

                Assert.That(pda, Is.Not.Null, $"{job} should get a PDA");
                Assert.That(GetPen(pda!.Value), Is.EqualTo("PenGreen"),
                    $"{job} should receive the pen chosen in the loadout");
            }
        });
    }

    [Test]
    public async Task JobsWithTheirOwnPenKeepItByDefault()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var cases = new (string Job, string Pen)[]
            {
                ("Clown", "CrayonOrange"),
                ("Librarian", "LuxuryPen"),
                ("Lawyer", "LuxuryPen"),
                ("Reporter", "LuxuryPen"),
            };

            var x = 0;
            foreach (var (job, pen) in cases)
            {
                var (_, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, x, 0), job);
                x += 2;

                Assert.That(pda, Is.Not.Null);
                Assert.That(GetPen(pda!.Value), Is.EqualTo(pen),
                    $"{job} should keep its own pen when no variation is selected");
            }
        });
    }

    [Test]
    public async Task HeadsKeepTheirUniquePens()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var x = 0;
            foreach (var (job, pen) in HeadPens)
            {
                var (_, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, x, 0), job);
                x += 2;

                Assert.That(pda, Is.Not.Null, $"{job} should get a PDA");
                Assert.That(GetPen(pda!.Value), Is.EqualTo(pen),
                    $"{job} should keep the unique pen of its PDA");
            }
        });
    }

    [Test]
    public async Task PenSpritesExistAndLookDifferent()
    {
        await Client.WaitAssertion(() =>
        {
            var cache = Client.ResolveDependency<Robust.Client.ResourceManagement.IResourceCache>();
            var rsi = cache.GetResource<Robust.Client.ResourceManagement.RSIResource>(
                new Robust.Shared.Utility.ResPath("/Textures/_VanGuard/Objects/Misc/pens.rsi"));

            var states = new HashSet<string>();
            foreach (var id in PortedPens.Keys)
            {
                var proto = CProtoMan.Index<EntityPrototype>(id);
                Assert.That(proto.TryComp<SpriteComponent>(out var sprite, CEntMan.ComponentFactory), Is.True,
                    $"{id} should have a sprite");

                var state = sprite!.LayerGetState(0);
                Assert.That(rsi.RSI.TryGetState(state, out _), Is.True,
                    $"{id} references the missing sprite state {state}");

                Assert.That(states.Add(state.ToString()), Is.True,
                    $"{id} must not share its sprite with another ported pen");
            }
        });
    }

}
