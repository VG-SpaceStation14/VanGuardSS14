#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Access.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Station.Systems;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Roles;

/// <summary>
///     Checks the RP PDA variations of jobs: the character keeps the job itself (access, department, salary),
///     while the chosen PDA and the job title shown on its ID card can differ.
/// </summary>
[TestFixture]
public sealed class JobPdaVariationTest : GameTest
{
    /// <summary>
    ///     Every job that is supposed to have RP PDA variations. The list is intentionally explicit so that
    ///     accidentally losing a variation is caught.
    /// </summary>
    private static readonly string[] ExpectedJobs =
    [
        "AtmosphericTechnician", "Bartender", "Botanist", "Brigmedic", "CargoTechnician", "Chaplain", "Chef",
        "ChiefMedicalOfficer", "Clown", "Detective", "Janitor", "Librarian", "MedicalIntern", "Musician",
        "Passenger", "Psychologist", "Quartermaster", "Reporter", "ResearchDirector", "ServiceWorker",
    ];

    private static readonly ProtoId<SpeciesPrototype> HumanSpecies = "Human";

    private sealed record PdaGroup(
        ProtoId<JobPrototype> Job,
        ProtoId<RoleLoadoutPrototype> Role,
        ProtoId<LoadoutGroupPrototype> Group,
        ProtoId<LoadoutPrototype> Default,
        EntProtoId DefaultPda,
        List<(ProtoId<LoadoutPrototype> Loadout, EntProtoId Pda)> Variants);

    private static HumanoidCharacterProfile CreateProfile() =>
        HumanoidCharacterProfile.DefaultWithSpecies(HumanSpecies, Sex.Male);

    private static IPrototypeManager ProtoMan => IoCManager.Instance!.Resolve<IPrototypeManager>();

    private static ILocalizationManager LocMan => IoCManager.Instance!.Resolve<ILocalizationManager>();

    private static string LocString(LocId id) => LocMan.GetString(id);

    /// <summary>
    ///     Collects every job PDA loadout group that offers more than the default PDA.
    ///     Loadouts with effects (like the senior PDAs) are skipped, as those are gated by playtime,
    ///     not by an RP choice.
    /// </summary>
    private static List<PdaGroup> GetPdaGroups()
    {
        var protoMan = ProtoMan;
        var factory = IoCManager.Instance!.Resolve<IComponentFactory>();
        var groups = new List<PdaGroup>();

        foreach (var role in protoMan.EnumeratePrototypes<RoleLoadoutPrototype>())
        {
            if (!role.ID.StartsWith("Job"))
                continue;

            var jobId = role.ID[3..];
            if (!protoMan.TryIndex<JobPrototype>(jobId, out var job))
                continue;

            foreach (var groupId in role.Groups)
            {
                if (!protoMan.TryIndex(groupId, out var groupProto))
                    continue;

                var pdaLoadouts = new List<(ProtoId<LoadoutPrototype> Loadout, EntProtoId Pda)>();
                foreach (var loadoutId in groupProto.Loadouts)
                {
                    if (!protoMan.TryIndex(loadoutId, out var loadout) || loadout.Effects.Count > 0)
                        continue;

                    if (!loadout.Equipment.TryGetValue("id", out var pdaId))
                        continue;

                    if (!protoMan.TryIndex(pdaId, out var pdaProto) ||
                        !pdaProto.TryComp<PdaComponent>(out _, factory))
                    {
                        continue;
                    }

                    pdaLoadouts.Add((loadoutId, pdaId));
                }

                if (pdaLoadouts.Count < 2)
                    continue;

                groups.Add(new PdaGroup(
                    job.ID,
                    role.ID,
                    groupId,
                    pdaLoadouts[0].Loadout,
                    pdaLoadouts[0].Pda,
                    pdaLoadouts.Skip(1).ToList()));
            }
        }

        return groups;
    }

    private HumanoidCharacterProfile CreateProfileWith(PdaGroup group, ProtoId<LoadoutPrototype> loadout)
    {
        var protoMan = ProtoMan;
        var profile = CreateProfile();
        var roleLoadout = new RoleLoadout(group.Role);
        roleLoadout.SetDefault(profile, ServerSession, protoMan, force: true);

        Assert.That(roleLoadout.AddLoadout(group.Group, loadout, protoMan), Is.True,
            $"Loadout {loadout} should be selectable in group {group.Group}");

        return profile.WithLoadout(roleLoadout);
    }

    /// <summary>
    ///     Spawns a character with the given job and returns the mob together with its ID slot item.
    /// </summary>
    private (EntityUid Mob, EntityUid? IdSlotItem) SpawnWithJob(EntityCoordinates coords,
        ProtoId<JobPrototype> job,
        HumanoidCharacterProfile? profile = null)
    {
        var mob = SEntMan.System<StationSpawningSystem>().SpawnPlayerMob(coords, job, profile ?? CreateProfile(), null);

        var inventory = SEntMan.System<InventorySystem>();
        inventory.TryGetSlotEntity(mob, "id", out var idSlotItem);
        return (mob, idSlotItem);
    }

    private EntityUid GetCard(EntityUid pda)
    {
        var containedId = SEntMan.GetComponent<PdaComponent>(pda).ContainedId;
        Assert.That(containedId, Is.Not.Null, "The PDA should contain an ID card");
        return containedId!.Value;
    }

    private string GetPdaPrototype(EntityUid pda) =>
        SEntMan.GetComponent<MetaDataComponent>(pda).EntityPrototype?.ID ?? string.Empty;

    /// <summary>
    ///     Counts the PDAs that belong to the mob, to make sure a variation does not hand out several of them.
    /// </summary>
    private int CountPdAs(EntityUid mob)
    {
        var count = 0;
        var children = SEntMan.GetComponent<TransformComponent>(mob).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (SEntMan.HasComponent<PdaComponent>(child))
                count++;
        }

        return count;
    }

    [Test]
    public async Task AllVariationsAreWellFormed()
    {
        await Server.WaitAssertion(() =>
        {
            var factory = SEntMan.ComponentFactory;
            var groups = GetPdaGroups();

            Assert.That(groups.Select(g => g.Job.Id), Is.SupersetOf(ExpectedJobs),
                "Every job from the old build that exists here should have a PDA variation group");

            Assert.That(groups.Sum(g => g.Variants.Count), Is.GreaterThanOrEqualTo(40),
                "All RP variations from the old build should be present");

            foreach (var group in groups)
            {
                var defaultPda = ProtoMan.Index(group.DefaultPda);
                Assert.That(defaultPda.TryComp<PdaComponent>(out var defaultPdaComp, factory), Is.True,
                    $"{group.DefaultPda} should be a PDA");
                Assert.That(defaultPdaComp!.IdCard, Is.Not.Null, $"{group.DefaultPda} should contain an ID card");

                foreach (var (loadoutId, pdaId) in group.Variants)
                {
                    var pda = ProtoMan.Index(pdaId);

                    Assert.That(pda.TryComp<PdaComponent>(out var pdaComp, factory), Is.True,
                        $"{pdaId} should be a PDA");
                    Assert.That(pdaComp!.IdCard, Is.Not.Null, $"{pdaId} should contain an ID card");

                    var card = ProtoMan.Index(pdaComp.IdCard!.Value);
                    Assert.That(card.TryComp<IdCardComponent>(out var idCard, factory), Is.True,
                        $"{pdaId} should contain an ID card");
                    Assert.That(idCard!.JobTitleOverride, Is.Not.Null,
                        $"{pdaId} should display an RP title instead of the job title");
                    Assert.That(LocMan.HasString(idCard.JobTitleOverride!.Value), Is.True,
                        $"Missing localization for {idCard.JobTitleOverride.Value}");

                    var loadout = ProtoMan.Index(loadoutId);
                    Assert.That(loadout.Equipment, Has.Count.EqualTo(1),
                        $"Loadout {loadoutId} should only hand out its PDA");
                }
            }
        });
    }

    [Test]
    public async Task EveryJobGivesItsDefaultPdaWithoutASelection()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var groups = GetPdaGroups();
            var x = 0;
            foreach (var group in groups)
            {
                var (mob, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, x, 0), group.Job);
                x += 2;

                Assert.That(pda, Is.Not.Null, $"{group.Job} should get a PDA");
                Assert.That(GetPdaPrototype(pda!.Value), Is.EqualTo(group.DefaultPda.Id),
                    $"{group.Job} should use its default PDA when no variation is selected");

                Assert.That(CountPdAs(mob), Is.EqualTo(1),
                    $"{group.Job} should only get its default PDA");

                var card = GetCard(pda.Value);
                Assert.That(SEntMan.GetComponent<IdCardComponent>(card).LocalizedJobTitle,
                    Is.EqualTo(ProtoMan.Index(group.Job).LocalizedName),
                    $"{group.Job} default card should show the job's own title");
            }
        });
    }

    [Test]
    public async Task EveryVariationSpawnsItsOwnPda()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var groups = GetPdaGroups();
            var x = 0;
            foreach (var group in groups)
            {
                // Reference spawn with the default PDA, to compare the job data with.
                var (_, defaultPda) = SpawnWithJob(new EntityCoordinates(map.Grid, x, 0), group.Job);
                x += 2;

                Assert.That(defaultPda, Is.Not.Null);
                var defaultCard = GetCard(defaultPda!.Value);
                var defaultAccess = SEntMan.GetComponent<AccessComponent>(defaultCard).Tags;
                var defaultDepartments = SEntMan.GetComponent<IdCardComponent>(defaultCard).JobDepartments;

                foreach (var (loadoutId, pdaId) in group.Variants)
                {
                    var profile = CreateProfileWith(group, loadoutId);
                    var (mob, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, x, 0), group.Job, profile);
                    x += 2;

                    Assert.That(pda, Is.Not.Null, $"{group.Job} should get a PDA for {loadoutId}");
                    Assert.That(GetPdaPrototype(pda!.Value), Is.EqualTo(pdaId.Id),
                        $"{loadoutId} should hand out {pdaId}");

                    Assert.That(CountPdAs(mob), Is.EqualTo(1),
                        $"{loadoutId} must not hand out the default PDA as well");

                    var card = GetCard(pda.Value);
                    var idCard = SEntMan.GetComponent<IdCardComponent>(card);

                    Assert.That(idCard.JobTitleOverride, Is.Not.Null);
                    Assert.That(idCard.LocalizedJobTitle, Is.EqualTo(LocString(idCard.JobTitleOverride!.Value)),
                        $"{loadoutId} should show its RP title on the ID card");

                    Assert.That(SEntMan.GetComponent<AccessComponent>(card).Tags, Is.EquivalentTo(defaultAccess),
                        $"{loadoutId} should keep the access of the base job");
                    Assert.That(idCard.JobDepartments, Is.EquivalentTo(defaultDepartments),
                        $"{loadoutId} should keep the department of the base job");
                }
            }
        });
    }

    [Test]
    public async Task SelectionSurvivesProfileValidation()
    {
        await Server.WaitAssertion(() =>
        {
            var protoMan = ProtoMan;
            var groups = GetPdaGroups().Where(g => g.Variants.Count > 0).Take(3);

            foreach (var group in groups)
            {
                var chosen = group.Variants[0].Loadout;
                var profile = CreateProfileWith(group, chosen);

                // Simulate a relog: the server validates stored loadouts and fills in default groups again.
                foreach (var (_, loadout) in profile.Loadouts)
                {
                    loadout.EnsureValid(profile, ServerSession!, IoCManager.Instance!);
                    loadout.SetDefault(profile, ServerSession, protoMan);
                }

                var reloaded = profile.GetLoadoutOrDefault(group.Role.Id, ServerSession, null, SEntMan, protoMan);

                Assert.That(reloaded.SelectedLoadouts[group.Group].Select(l => l.Prototype.Id),
                    Is.EquivalentTo(new[] { chosen.Id }),
                    $"{chosen} should survive profile validation for {group.Job}");
            }
        });
    }

    [Test]
    public async Task MissingVariationFallsBackToTheDefaultPda()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var group = GetPdaGroups().First(g => g.Job.Id == "Bartender");
            var profile = CreateProfile();
            var protoMan = ProtoMan;

            var loadout = new RoleLoadout(group.Role);
            loadout.SetDefault(profile, ServerSession, protoMan, force: true);

            // Pretend the variation prototype got removed from the game.
            loadout.SelectedLoadouts[group.Group] = [new Loadout { Prototype = "RemovedVariationPDA" }];
            loadout.EnsureValid(profile, ServerSession!, IoCManager.Instance!);

            profile.SetLoadout(loadout);

            var (_, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, 0, 0), group.Job, profile);

            Assert.That(pda, Is.Not.Null, "The character should still get a PDA");
            Assert.That(GetPdaPrototype(pda!.Value), Is.EqualTo(group.DefaultPda.Id),
                "A missing variation should fall back to the default job PDA");
        });
    }

    [Test]
    public async Task ProfileWithoutVariationGroupFallsBackToTheDefaultPda()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var group = GetPdaGroups().First(g => g.Job.Id == "Chef");
            var protoMan = ProtoMan;
            var profile = CreateProfile();

            var loadout = new RoleLoadout(group.Role);
            loadout.SetDefault(profile, ServerSession, protoMan, force: true);

            // Simulate a profile saved before the variation group was added to the job.
            loadout.SelectedLoadouts.Remove(group.Group);
            loadout.EnsureValid(profile, ServerSession!, IoCManager.Instance!);
            profile.SetLoadout(loadout);

            Assert.That(loadout.SelectedLoadouts[group.Group].Select(l => l.Prototype.Id),
                Is.EquivalentTo(new[] { group.Default.Id }),
                "An old profile should be defaulted to the base PDA");

            var (_, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, 0, 0), group.Job, profile);

            Assert.That(pda, Is.Not.Null, "The character should still get a PDA");
            Assert.That(GetPdaPrototype(pda!.Value), Is.EqualTo(group.DefaultPda.Id),
                "An old profile should spawn with the default job PDA");
        });
    }

    [Test]
    public async Task JobWithoutVariationsStillGetsItsPda()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            // Chemist was not touched by the variation work, its PDA still comes from the starting gear.
            var (_, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, 0, 0), "Chemist");

            Assert.That(pda, Is.Not.Null, "Jobs without variations should still get their PDA");
            Assert.That(GetPdaPrototype(pda!.Value), Is.EqualTo("ChemistryPDA"),
                "Jobs without variations should keep their usual PDA");

            Assert.That(GetPdaGroups().Any(g => g.Job.Id == "Chemist"), Is.False,
                "Chemist should not be considered a job with RP variations");
        });
    }
}
