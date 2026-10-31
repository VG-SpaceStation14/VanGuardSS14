#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._VanGuard.Quirks.Components;
using Content.Shared.Mind;
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
    private const string CategoryId = "CharacterQuirks";
    private const int CategoryPoints = 5;
    private const string QuirkPrefix = "Quirk";

    private static readonly ProtoId<TraitCategoryPrototype> PrivacyCategory = "PrivacyTraits";
    private static readonly ProtoId<TraitPrototype> IncognitoQuirk = "QuirkIncognito";

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

            Assert.That(quirks, Has.Count.EqualTo(13), "All VanGuard quirks should be loaded");
            Assert.That(Proto.TryIndex<TraitCategoryPrototype>(CategoryId, out var category), Is.True,
                "The quirk category should exist");
            Assert.That(category!.MaxTraitPoints, Is.EqualTo(CategoryPoints));
            Assert.That(category.Priority, Is.EqualTo(5), "The quirk category should be shown first in the tab");
            Assert.That(category.AccentColor, Is.Not.EqualTo(default(Robust.Shared.Maths.Color)),
                "The quirk category should have an accent color");

            // Upstream categories are kept and shown by the same quirk tab.
            foreach (var upstreamCategory in new[] { "Disabilities", "SpeechTraits", "Quirks" })
            {
                Assert.That(Proto.HasIndex<TraitCategoryPrototype>(upstreamCategory), Is.True,
                    $"The upstream trait category {upstreamCategory} should be preserved");
            }

            // Incognito is an account-level quirk: free, shown first, and it has a real effect.
            Assert.That(Proto.TryIndex(PrivacyCategory, out var privacy), Is.True);
            Assert.That(privacy!.Priority, Is.LessThan(category.Priority),
                "The privacy category should be shown before the other quirk categories");
            Assert.That(Proto.TryIndex(IncognitoQuirk, out var incognito), Is.True);
            Assert.That(incognito!.Cost, Is.Zero, "The incognito quirk should be free");
            Assert.That(incognito.Specials.Count, Is.GreaterThan(0), "The incognito quirk should apply a component");

            var byId = quirks.ToDictionary(q => q.ID, q => q);

            foreach (var quirk in quirks)
            {
                Assert.That(quirk.Category?.ToString(), Is.AnyOf(CategoryId, PrivacyCategory.Id),
                    $"{quirk.ID} should be in one of the quirk categories");
                if (quirk.Category?.ToString() == CategoryId)
                {
                    Assert.That(quirk.Cost, Is.Not.Zero,
                        $"{quirk.ID} should affect the trait point balance");
                }
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
            var profile = WithQuirks(CreateProfile(), "QuirkFleetFooted", "QuirkQuickClot");
            Assert.That(Selected(profile), Is.EquivalentTo(new[] { "QuirkFleetFooted", "QuirkQuickClot" }));

            // The profile is copied and serialized between the editor, the database and the server.
            var copy = new HumanoidCharacterProfile(profile);
            Assert.That(Selected(copy), Is.EquivalentTo(new[] { "QuirkFleetFooted", "QuirkQuickClot" }));

            var valid = copy.GetValidTraits(copy.TraitPreferences, Proto);
            Assert.That(valid.Select(t => t.Id), Is.EquivalentTo(new[] { "QuirkFleetFooted", "QuirkQuickClot" }),
                "A saved valid selection should survive validation");

            var removed = copy.WithoutTraitPreference("QuirkFleetFooted", Proto);
            Assert.That(Selected(removed), Is.EquivalentTo(new[] { "QuirkQuickClot" }));
        });
    }

    [Test]
    public async Task ConflictingQuirksAreRejected()
    {
        await Server.WaitAssertion(() =>
        {
            var withFleet = WithQuirks(CreateProfile(), "QuirkFleetFooted");
            var both = withFleet.WithTraitPreference("QuirkHeavyGait", Proto);
            Assert.That(Selected(both), Has.No.Member("QuirkHeavyGait"),
                "A conflicting quirk should not be added");

            var withHeavy = WithQuirks(CreateProfile(), "QuirkHeavyGait");
            Assert.That(Selected(withHeavy.WithTraitPreference("QuirkFleetFooted", Proto)),
                Has.No.Member("QuirkFleetFooted"),
                "Conflicts should be checked in both directions");

            // A VanGuard quirk can also conflict with an existing trait of the build.
            var withClearHead = WithQuirks(CreateProfile(), "QuirkClearHead");
            Assert.That(Selected(withClearHead.WithTraitPreference("LightweightDrunk", Proto)),
                Has.No.Member("LightweightDrunk"),
                "The alcohol quirk should conflict with the existing lightweight drunk trait");

            var invalid = CreateProfile().GetValidTraits(
                new ProtoId<TraitPrototype>[] { "QuirkFleetFooted", "QuirkHeavyGait" }, Proto);
            Assert.That(invalid, Has.Count.EqualTo(1),
                "Validation should drop conflicting quirks that were saved by an older version");
        });
    }

    [Test]
    public async Task TraitPointBudgetIsEnforced()
    {
        await Server.WaitAssertion(() =>
        {
            var maxed = WithQuirks(CreateProfile(), "QuirkFleetFooted", "QuirkSturdyNerves");
            Assert.That(Selected(maxed), Has.Length.EqualTo(2), "Both positive quirks fit into the budget");

            var overBudget = maxed.WithTraitPreference("QuirkClearHead", Proto);
            Assert.That(Selected(overBudget), Has.No.Member("QuirkClearHead"),
                "A quirk that exceeds the category point limit should be rejected");

            // A negative quirk refunds points and makes room for another positive one.
            var withRefund = WithQuirks(CreateProfile(), "QuirkFragile", "QuirkFleetFooted",
                "QuirkSturdyNerves", "QuirkClearHead");
            Assert.That(Selected(withRefund), Has.Length.EqualTo(4),
                "A drawback should refund trait points for the positives");

            var valid = CreateProfile().GetValidTraits(
                new ProtoId<TraitPrototype>[] { "QuirkFleetFooted", "QuirkSturdyNerves", "QuirkClearHead" }, Proto);
            Assert.That(valid.Select(t => t.Id), Is.EquivalentTo(new[] { "QuirkFleetFooted", "QuirkSturdyNerves" }),
                "Validation should keep only the quirks that fit into the budget");
        });
    }

    [Test]
    public async Task SpeciesRestrictionsAreEnforced()
    {
        await Server.WaitAssertion(() =>
        {
            var diona = CreateProfile("Diona");
            Assert.That(Selected(WithQuirks(diona, "QuirkColdHardened")), Has.No.Member("QuirkColdHardened"),
                "Species from the blacklist should not get the quirk");
            Assert.That(Selected(WithQuirks(diona, "QuirkRadHardened")), Does.Contain("QuirkRadHardened"),
                "Species from the whitelist should get the quirk");

            var human = CreateProfile();
            Assert.That(Selected(WithQuirks(human, "QuirkColdHardened")), Does.Contain("QuirkColdHardened"));
            Assert.That(Selected(WithQuirks(human, "QuirkRadHardened")), Has.No.Member("QuirkRadHardened"),
                "The whitelist quirk is not available for other species");

            // Validation of a saved profile with a species change should drop the now restricted quirk.
            var wrongSpecies = CreateProfile().WithTraitPreference("QuirkRadHardened", Proto);
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
                WithQuirks(CreateProfile(), "QuirkFleetFooted"));
            Assert.That(SEntMan.GetComponent<QuirkMovementSpeedComponent>(speedy).SprintModifier,
                Is.EqualTo(1.07f).Within(0.001f));

            var sturdy = SpawnWithQuirks(new EntityCoordinates(map.Grid, 2, 0),
                WithQuirks(CreateProfile(), "QuirkSturdyNerves"));
            var stamina = SEntMan.GetComponent<StaminaResistanceComponent>(sturdy);
            Assert.That(stamina.DamageCoefficient, Is.EqualTo(0.85f).Within(0.001f));
            Assert.That(stamina.Worn, Is.False, "The resistance should protect the mob itself, not its clothes");

            var cold = SpawnWithQuirks(new EntityCoordinates(map.Grid, 4, 0),
                WithQuirks(CreateProfile(), "QuirkColdHardened"));
            var resistances = SEntMan.GetComponent<QuirkDamageTypeResistanceComponent>(cold);
            Assert.That(resistances.Modifiers["Cold"], Is.EqualTo(0.75f).Within(0.001f));

            var sober = SpawnWithQuirks(new EntityCoordinates(map.Grid, 6, 0),
                WithQuirks(CreateProfile(), "QuirkClearHead"));
            Assert.That(SEntMan.HasComponent<LightweightDrunkComponent>(sober), Is.True);

            var clotting = SpawnWithQuirks(new EntityCoordinates(map.Grid, 8, 0),
                WithQuirks(CreateProfile(), "QuirkQuickClot"));
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
            var profile = WithQuirks(CreateProfile(), "QuirkFleetFooted");
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
                WithQuirks(CreateProfile(), "QuirkFleetFooted"));

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

    [Test]
    public async Task IncognitoQuirkMarksTheMind()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var profile = CreateProfile().WithTraitPreference("QuirkIncognito", Proto);
            Assert.That(Selected(profile), Does.Contain("QuirkIncognito"));

            var mob = SEntMan.System<StationSpawningSystem>().SpawnPlayerMob(
                new EntityCoordinates(map.Grid, 0, 0), "Passenger", profile, null);

            // Character spawning creates the mind before the spawn-complete event is raised.
            var mindSystem = SEntMan.System<SharedMindSystem>();
            var mind = mindSystem.CreateMind(ServerSession!.UserId, "Incognito Test");
            mindSystem.TransferTo(mind, mob, ghostCheckOverride: true);

            var ev = new PlayerSpawnCompleteEvent(mob, ServerSession, "Passenger", false, true, 1, EntityUid.Invalid, profile);
            Server.EntMan.EventBus.RaiseEvent(EventSource.Local, ev);

            Assert.That(SEntMan.HasComponent<IncognitoComponent>(mob), Is.True,
                "The incognito quirk should add its component to the character");
            Assert.That(SEntMan.HasComponent<IncognitoComponent>(mind), Is.True,
                "The mind should stay incognito so the round-end list can hide the nickname");

            // A character without the quirk must not become incognito.
            var normalProfile = CreateProfile().WithTraitPreference("Pacifist", Proto);
            var normalMob = SEntMan.System<StationSpawningSystem>().SpawnPlayerMob(
                new EntityCoordinates(map.Grid, 2, 0), "Passenger", normalProfile, null);
            var normalMind = mindSystem.CreateMind(ServerSession.UserId, "Normal Test");
            mindSystem.TransferTo(normalMind, normalMob, ghostCheckOverride: true);
            var normalEv = new PlayerSpawnCompleteEvent(normalMob, ServerSession, "Passenger", false, true, 1, EntityUid.Invalid, normalProfile);
            Server.EntMan.EventBus.RaiseEvent(EventSource.Local, normalEv);

            Assert.That(SEntMan.HasComponent<IncognitoComponent>(normalMind), Is.False);
        });
    }

}
