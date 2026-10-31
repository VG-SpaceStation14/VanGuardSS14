using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Access.Components;
using Content.Shared.Access;
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
///     Checks the RP PDA variations of a job: the character keeps the job itself (access, department, salary),
///     while the chosen PDA and the job title shown on its ID card can differ.
/// </summary>
[TestFixture]
public sealed class JobPdaVariationTest : GameTest
{
    private static readonly ProtoId<JobPrototype> Job = "CargoTechnician";
    private static readonly ProtoId<RoleLoadoutPrototype> RoleLoadout = "JobCargoTechnician";
    private static readonly ProtoId<LoadoutGroupPrototype> PdaGroup = "CargoTechnicianID";
    private static readonly ProtoId<LoadoutPrototype> VariantPdaLoadout = "PostmanPDA";
    private static readonly ProtoId<SpeciesPrototype> HumanSpecies = "Human";
    private static readonly EntProtoId DefaultPda = "CargoPDA";
    private static readonly EntProtoId VariantPda = "PostmanPDA";
    private static readonly LocId VariantTitle = "job-title-postman";

    private static HumanoidCharacterProfile CreateProfile() =>
        HumanoidCharacterProfile.DefaultWithSpecies(HumanSpecies, Sex.Male);

    private static HumanoidCharacterProfile CreateProfileWithVariant()
    {
        var profile = CreateProfile();
        var protoMan = IoCManager.Instance!.Resolve<IPrototypeManager>();

        var loadout = new RoleLoadout(RoleLoadout);
        loadout.SetDefault(profile, null, protoMan, force: true);
        Assert.That(loadout.AddLoadout(PdaGroup, VariantPdaLoadout, protoMan), Is.True,
            "The variation should be selectable in the job's PDA loadout group");

        return profile.WithLoadout(loadout);
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

    private static string LocString(LocId id) =>
        IoCManager.Instance!.Resolve<ILocalizationManager>().GetString(id);

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
    public async Task DefaultPdaIsUsedWhenNoVariationIsSelected()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var (_, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, 0, 0), Job);

            Assert.That(pda, Is.Not.Null, "The character should get a PDA");
            Assert.That(GetPdaPrototype(pda!.Value), Is.EqualTo(DefaultPda.Id),
                "Without a variation the default job PDA should be used");

            var card = GetCard(pda.Value);
            Assert.That(SEntMan.GetComponent<IdCardComponent>(card).LocalizedJobTitle,
                Is.EqualTo(LocString("job-name-cargotech")),
                "The default card should show the job's own title");
        });
    }

    [Test]
    public async Task SelectedVariationReplacesTheDefaultPda()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var (mob, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, 0, 0), Job, CreateProfileWithVariant());

            Assert.That(pda, Is.Not.Null, "The character should get a PDA");
            Assert.That(GetPdaPrototype(pda!.Value), Is.EqualTo(VariantPda.Id),
                "The chosen variation PDA should be used");

            Assert.That(CountPdAs(mob), Is.EqualTo(1),
                "Selecting a variation must not hand out the default PDA as well");

            var card = GetCard(pda.Value);
            var idCard = SEntMan.GetComponent<IdCardComponent>(card);

            Assert.That(idCard.LocalizedJobTitle, Is.EqualTo(LocString(VariantTitle)),
                "The variation title should be shown on the ID card");
        });
    }

    [Test]
    public async Task VariationKeepsTheBaseJobData()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var (_, defaultPda) = SpawnWithJob(new EntityCoordinates(map.Grid, 0, 0), Job);
            var (_, variantPda) = SpawnWithJob(new EntityCoordinates(map.Grid, 2, 0), Job, CreateProfileWithVariant());

            Assert.That(defaultPda, Is.Not.Null);
            Assert.That(variantPda, Is.Not.Null);

            var defaultCard = SEntMan.GetComponent<IdCardComponent>(GetCard(defaultPda!.Value));
            var variantCard = SEntMan.GetComponent<IdCardComponent>(GetCard(variantPda!.Value));

            var presetCard = SEntMan.GetComponent<PresetIdCardComponent>(GetCard(variantPda.Value));
            Assert.That(presetCard.JobName, Is.EqualTo(Job),
                "The variation card should still belong to the base job");
            Assert.That(variantCard.JobDepartments, Is.EquivalentTo(defaultCard.JobDepartments),
                "The variation should keep the department of the base job");

            var defaultAccess = SEntMan.GetComponent<AccessComponent>(GetCard(defaultPda.Value));
            var variantAccess = SEntMan.GetComponent<AccessComponent>(GetCard(variantPda.Value));

            Assert.That(variantAccess.Tags, Is.EquivalentTo(defaultAccess.Tags),
                "The variation should keep the access of the base job");
            Assert.That(variantAccess.Tags, Does.Contain(new ProtoId<AccessLevelPrototype>("Cargo")),
                "The variation card should still have the job's access");
        });
    }

    [Test]
    public async Task SelectionSurvivesProfileValidation()
    {
        await Server.WaitAssertion(() =>
        {
            var profile = CreateProfileWithVariant();

            // Simulate a relog: the server validates stored loadouts and fills in default groups again.
            foreach (var (_, loadout) in profile.Loadouts)
            {
                loadout.EnsureValid(profile, null!, IoCManager.Instance!);
                loadout.SetDefault(profile, null, IoCManager.Instance!.Resolve<IPrototypeManager>());
            }

            var reloaded = profile.GetLoadoutOrDefault(RoleLoadout.Id,
                null,
                null,
                SEntMan,
                IoCManager.Instance!.Resolve<IPrototypeManager>());

            Assert.That(reloaded.SelectedLoadouts[PdaGroup].Select(x => x.Prototype.Id),
                Is.EquivalentTo(new[] { VariantPdaLoadout.Id }),
                "The chosen variation should survive profile validation");
        });
    }

    [Test]
    public async Task MissingVariationFallsBackToTheDefaultPda()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var profile = CreateProfile();
            var protoMan = IoCManager.Instance!.Resolve<IPrototypeManager>();

            var loadout = new RoleLoadout(RoleLoadout);
            loadout.SetDefault(profile, null, protoMan, force: true);

            // Pretend the variation prototype got removed from the game.
            loadout.SelectedLoadouts[PdaGroup] = [new Loadout { Prototype = "RemovedVariationPDA" }];
            loadout.EnsureValid(profile, null!, IoCManager.Instance!);

            profile.SetLoadout(loadout);

            var (_, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, 0, 0), Job, profile);

            Assert.That(pda, Is.Not.Null, "The character should still get a PDA");
            Assert.That(GetPdaPrototype(pda!.Value), Is.EqualTo(DefaultPda.Id),
                "A missing variation should fall back to the default job PDA");
        });
    }

    [Test]
    public async Task JobWithoutVariationsStillGetsItsPda()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var (_, pda) = SpawnWithJob(new EntityCoordinates(map.Grid, 0, 0), "Passenger");

            Assert.That(pda, Is.Not.Null, "Jobs without variations should still get their PDA");
            Assert.That(GetPdaPrototype(pda!.Value), Is.EqualTo("PassengerPDA"),
                "Jobs without variations should keep their usual PDA");
        });
    }
}
