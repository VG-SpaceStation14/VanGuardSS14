#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._VanGuard.Quirks.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Damage.Components;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Preferences;
using Content.Shared.Station.Systems;
using Content.Shared.Traits;
using Content.Shared.Traits.Assorted;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Quirks;

/// <summary>
///     Checks the VanGuard character quirks: prototypes, selection rules, saving and the applied effects.
/// </summary>
[TestFixture]
public sealed class QuirkSystemTest : GameTest
{
    private const string CategoryId = "VGQuirks";
    private const int CategoryPoints = 5;
    private const string QuirkPrefix = "VGQuirk";

    private static IPrototypeManager Proto => IoCManager.Instance!.Resolve<IPrototypeManager>();

    private static ILocalizationManager Loc => IoCManager.Instance!.Resolve<ILocalizationManager>();

    private static HumanoidCharacterProfile CreateProfile(string species = "Human") =>
        HumanoidCharacterProfile.DefaultWithSpecies(species, Sex.Male);

    private static HumanoidCharacterProfile WithQuirks(HumanoidCharacterProfile profile, params string[] ids)
    {
        foreach (var id in ids)
            profile = profile.WithTraitPreference(id, Proto);

        return profile;
    }

    private static string[] Selected(HumanoidCharacterProfile profile) =>
        profile.TraitPreferences.Select(t => t.Id).OrderBy(t => t).ToArray();

    private EntityUid SpawnWithQuirks(EntityCoordinates coords, HumanoidCharacterProfile profile)
    {
        var mob = SEntMan.System<StationSpawningSystem>().SpawnPlayerMob(coords, "Passenger", profile, null);
        RaiseSpawnComplete(mob, profile);
        return mob;
    }

    private void RaiseSpawnComplete(EntityUid mob, HumanoidCharacterProfile profile)
    {
        var ev = new PlayerSpawnCompleteEvent(mob, ServerSession!, "Passenger", false, true, 1, EntityUid.Invalid, profile);
        Server.EntMan.EventBus.RaiseEvent(EventSource.Local, ev);
    }

    [Test]
    public async Task QuirkPrototypesAreValid()
    {
        await Server.WaitAssertion(() =>
        {
            var quirks = Proto.EnumeratePrototypes<TraitPrototype>()
                .Where(p => p.ID.StartsWith(QuirkPrefix, System.StringComparison.Ordinal))
                .ToList();

            Assert.That(quirks, Has.Count.EqualTo(12), "All VanGuard quirks should be loaded");
            Assert.That(Proto.TryIndex<TraitCategoryPrototype>(CategoryId, out var category), Is.True,
                "The quirk category should exist");
            Assert.That(category!.MaxTraitPoints, Is.EqualTo(CategoryPoints));

            var byId = quirks.ToDictionary(q => q.ID, q => q);

            foreach (var quirk in quirks)
            {
                Assert.That(quirk.Category?.ToString(), Is.EqualTo(CategoryId), $"{quirk.ID} should be in the quirk category");
                Assert.That(quirk.Cost, Is.Not.Zero, $"{quirk.ID} should affect the trait point balance");
                Assert.That(Loc.HasString(quirk.Name), Is.True, $"Missing name localization for {quirk.ID}");
                Assert.That(quirk.Description, Is.Not.Null, $"{quirk.ID} should have a description");
                Assert.That(Loc.HasString(quirk.Description!.Value), Is.True, $"Missing description localization for {quirk.ID}");

                Assert.That(quirk.Specials.Count + quirk.Components.Count, Is.GreaterThan(0),
                    $"{quirk.ID} should have a real gameplay effect");

                foreach (var species in quirk.SpeciesBlacklist.Concat(quirk.SpeciesWhitelist))
                    Assert.That(Proto.HasIndex<SpeciesPrototype>(species), Is.True, $"{quirk.ID} references unknown species {species}");

                foreach (var conflict in quirk.Conflicts)
                {
                    Assert.That(Proto.HasIndex<TraitPrototype>(conflict), Is.True,
                        $"{quirk.ID} references unknown conflict {conflict}");

                    // Conflicts declared between our own quirks have to be mutual.
                    if (byId.TryGetValue(conflict.Id, out var other))
                    {
                        Assert.That(other.Conflicts.Select(c => c.Id), Does.Contain(quirk.ID),
                            $"{quirk.ID} and {conflict.Id} should declare each other as conflicts");
                    }
                }
            }
        });
    }

    [Test]
    public async Task QuirkSelectionIsSavedAndRestored()
    {
        await Server.WaitAssertion(() =>
        {
            var profile = WithQuirks(CreateProfile(), "VGQuirkFleetFooted", "VGQuirkQuickClot");
            Assert.That(Selected(profile), Is.EquivalentTo(new[] { "VGQuirkFleetFooted", "VGQuirkQuickClot" }));

            // The profile is copied and serialized between the editor, the database and the server.
            var copy = new HumanoidCharacterProfile(profile);
            Assert.That(Selected(copy), Is.EquivalentTo(new[] { "VGQuirkFleetFooted", "VGQuirkQuickClot" }));

            var valid = copy.GetValidTraits(copy.TraitPreferences, Proto);
            Assert.That(valid.Select(t => t.Id), Is.EquivalentTo(new[] { "VGQuirkFleetFooted", "VGQuirkQuickClot" }),
                "A saved valid selection should survive validation");

            var removed = copy.WithoutTraitPreference("VGQuirkFleetFooted", Proto);
            Assert.That(Selected(removed), Is.EquivalentTo(new[] { "VGQuirkQuickClot" }));
        });
    }

    [Test]
    public async Task ConflictingQuirksAreRejected()
    {
        await Server.WaitAssertion(() =>
        {
            var withFleet = WithQuirks(CreateProfile(), "VGQuirkFleetFooted");
            var both = withFleet.WithTraitPreference("VGQuirkHeavyGait", Proto);
            Assert.That(Selected(both), Has.No.Member("VGQuirkHeavyGait"),
                "A conflicting quirk should not be added");

            var withHeavy = WithQuirks(CreateProfile(), "VGQuirkHeavyGait");
            Assert.That(Selected(withHeavy.WithTraitPreference("VGQuirkFleetFooted", Proto)),
                Has.No.Member("VGQuirkFleetFooted"),
                "Conflicts should be checked in both directions");

            // A VanGuard quirk can also conflict with an existing trait of the build.
            var withClearHead = WithQuirks(CreateProfile(), "VGQuirkClearHead");
            Assert.That(Selected(withClearHead.WithTraitPreference("LightweightDrunk", Proto)),
                Has.No.Member("LightweightDrunk"),
                "The alcohol quirk should conflict with the existing lightweight drunk trait");

            var invalid = CreateProfile().GetValidTraits(
                new ProtoId<TraitPrototype>[] { "VGQuirkFleetFooted", "VGQuirkHeavyGait" }, Proto);
            Assert.That(invalid, Has.Count.EqualTo(1),
                "Validation should drop conflicting quirks that were saved by an older version");
        });
    }

    [Test]
    public async Task TraitPointBudgetIsEnforced()
    {
        await Server.WaitAssertion(() =>
        {
            var maxed = WithQuirks(CreateProfile(), "VGQuirkFleetFooted", "VGQuirkSturdyNerves");
            Assert.That(Selected(maxed), Has.Length.EqualTo(2), "Both positive quirks fit into the budget");

            var overBudget = maxed.WithTraitPreference("VGQuirkClearHead", Proto);
            Assert.That(Selected(overBudget), Has.No.Member("VGQuirkClearHead"),
                "A quirk that exceeds the category point limit should be rejected");

            // A negative quirk refunds points and makes room for another positive one.
            var withRefund = WithQuirks(CreateProfile(), "VGQuirkFragile", "VGQuirkFleetFooted",
                "VGQuirkSturdyNerves", "VGQuirkClearHead");
            Assert.That(Selected(withRefund), Has.Length.EqualTo(4),
                "A drawback should refund trait points for the positives");

            var valid = CreateProfile().GetValidTraits(
                new ProtoId<TraitPrototype>[] { "VGQuirkFleetFooted", "VGQuirkSturdyNerves", "VGQuirkClearHead" }, Proto);
            Assert.That(valid.Select(t => t.Id), Is.EquivalentTo(new[] { "VGQuirkFleetFooted", "VGQuirkSturdyNerves" }),
                "Validation should keep only the quirks that fit into the budget");
        });
    }

    [Test]
    public async Task SpeciesRestrictionsAreEnforced()
    {
        await Server.WaitAssertion(() =>
        {
            var diona = CreateProfile("Diona");
            Assert.That(Selected(WithQuirks(diona, "VGQuirkColdHardened")), Has.No.Member("VGQuirkColdHardened"),
                "Species from the blacklist should not get the quirk");
            Assert.That(Selected(WithQuirks(diona, "VGQuirkRadHardened")), Does.Contain("VGQuirkRadHardened"),
                "Species from the whitelist should get the quirk");

            var human = CreateProfile();
            Assert.That(Selected(WithQuirks(human, "VGQuirkColdHardened")), Does.Contain("VGQuirkColdHardened"));
            Assert.That(Selected(WithQuirks(human, "VGQuirkRadHardened")), Has.No.Member("VGQuirkRadHardened"),
                "The whitelist quirk is not available for other species");

            // Validation of a saved profile with a species change should drop the now restricted quirk.
            var wrongSpecies = CreateProfile().WithTraitPreference("VGQuirkRadHardened", Proto);
            Assert.That(wrongSpecies.GetValidTraits(wrongSpecies.TraitPreferences, Proto), Is.Empty);
        });
    }

    [Test]
    public async Task QuirkEffectsAreAppliedOnSpawn()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var speedy = SpawnWithQuirks(new EntityCoordinates(map.Grid, 0, 0),
                WithQuirks(CreateProfile(), "VGQuirkFleetFooted"));
            Assert.That(SEntMan.GetComponent<QuirkMovementSpeedComponent>(speedy).SprintModifier,
                Is.EqualTo(1.07f).Within(0.001f));

            var sturdy = SpawnWithQuirks(new EntityCoordinates(map.Grid, 2, 0),
                WithQuirks(CreateProfile(), "VGQuirkSturdyNerves"));
            var stamina = SEntMan.GetComponent<StaminaResistanceComponent>(sturdy);
            Assert.That(stamina.DamageCoefficient, Is.EqualTo(0.85f).Within(0.001f));
            Assert.That(stamina.Worn, Is.False, "The resistance should protect the mob itself, not its clothes");

            var cold = SpawnWithQuirks(new EntityCoordinates(map.Grid, 4, 0),
                WithQuirks(CreateProfile(), "VGQuirkColdHardened"));
            var resistances = SEntMan.GetComponent<QuirkDamageTypeResistanceComponent>(cold);
            Assert.That(resistances.Modifiers["Cold"], Is.EqualTo(0.75f).Within(0.001f));

            var sober = SpawnWithQuirks(new EntityCoordinates(map.Grid, 6, 0),
                WithQuirks(CreateProfile(), "VGQuirkClearHead"));
            Assert.That(SEntMan.HasComponent<LightweightDrunkComponent>(sober), Is.True);

            var clotting = SpawnWithQuirks(new EntityCoordinates(map.Grid, 8, 0),
                WithQuirks(CreateProfile(), "VGQuirkQuickClot"));
            Assert.That(SEntMan.GetComponent<QuirkBleedRateComponent>(clotting).BleedMultiplier,
                Is.EqualTo(0.75f).Within(0.001f));
        });
    }

    [Test]
    public async Task QuirkEffectsAreNotAppliedTwice()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var profile = WithQuirks(CreateProfile(), "VGQuirkFleetFooted");
            var mob = SpawnWithQuirks(new EntityCoordinates(map.Grid, 0, 0), profile);

            // Simulate a repeated spawn event for the same mob (bad data or a double spawn).
            RaiseSpawnComplete(mob, profile);

            Assert.That(SEntMan.Count<QuirkMovementSpeedComponent>(), Is.EqualTo(1),
                "The quirk component should exist exactly once");

            var movement = SEntMan.System<MovementSpeedModifierSystem>();
            movement.RefreshMovementSpeedModifiers(mob);
            var speed = SEntMan.GetComponent<MovementSpeedModifierComponent>(mob);
            Assert.That(speed.SprintSpeedModifier, Is.EqualTo(1.07f).Within(0.001f),
                "Repeated application must not stack the speed modifier");
        });
    }

    [Test]
    public async Task QuirkSpeedModifierIsActuallyApplied()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var control = SpawnWithQuirks(new EntityCoordinates(map.Grid, 0, 0), CreateProfile());
            var speedy = SpawnWithQuirks(new EntityCoordinates(map.Grid, 2, 0),
                WithQuirks(CreateProfile(), "VGQuirkFleetFooted"));

            var movement = SEntMan.System<MovementSpeedModifierSystem>();
            movement.RefreshMovementSpeedModifiers(control);
            movement.RefreshMovementSpeedModifiers(speedy);

            var controlSpeed = SEntMan.GetComponent<MovementSpeedModifierComponent>(control);
            var speedySpeed = SEntMan.GetComponent<MovementSpeedModifierComponent>(speedy);

            Assert.That(speedySpeed.SprintSpeedModifier, Is.GreaterThan(controlSpeed.SprintSpeedModifier),
                "The speed quirk should make the character sprint faster than an unmodified character");
        });
    }

    [Test]
    public async Task ExistingTraitsStillWork()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var profile = WithQuirks(CreateProfile(), "Pacifist", "Snoring");
            Assert.That(Selected(profile), Is.EquivalentTo(new[] { "Pacifist", "Snoring" }));

            var mob = SpawnWithQuirks(new EntityCoordinates(map.Grid, 0, 0), profile);
            Assert.That(SEntMan.HasComponent<PacifiedComponent>(mob), Is.True,
                "Existing traits should still be applied together with the quirks");
            Assert.That(SEntMan.HasComponent<SnoringComponent>(mob), Is.True);
        });
    }
}
