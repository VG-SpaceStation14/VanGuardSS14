#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared._VanGuard.Language;
using Content.Shared.Humanoid;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Language;

/// <summary>
///     Verifies that non-player mobs (species mobs, animals, slimes, etc.) receive
///     their language component and native languages on spawn.
/// </summary>
[TestFixture]
public sealed class LanguageMobTest : GameTest
{
    [Test]
    public async Task SpeciesMob_GetsItsDefaultLanguages()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();

        var coords = new EntityCoordinates(testMap.MapUid, default);

        // Spawn a non-player Vulpkanin (admin-spawned, not through the player pipeline).
        var vulp = await SpawnAndWait(server, entMan, coords, "MobVulpkanin");

        Assert.That(entMan.HasComponent<LanguageSpeakerComponent>(vulp), Is.True,
            "Species mob should have a LanguageSpeakerComponent.");
        var comp = entMan.GetComponent<LanguageSpeakerComponent>(vulp);
        Assert.That(comp.Languages.Keys, Does.Contain("GalacticCommon"),
            "Vulpkanin should speak GalacticCommon.");
        Assert.That(comp.Languages.Keys, Does.Contain("Canilunzt"),
            "Vulpkanin should speak its species language Canilunzt.");

        // Sanity check: the species is known from the HumanoidProfileComponent.
        Assert.That(entMan.GetComponent<HumanoidProfileComponent>(vulp).Species.Id, Is.EqualTo("Vulpkanin"));
    }

    [Test]
    public async Task AnimalMobs_GetTheirLanguages()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();

        var coords = new EntityCoordinates(testMap.MapUid, default);

        // (prototype, expected language)
        var cases = new (string Mob, string Language)[]
        {
            ("MobCat", "Cat"),
            ("MobCorgi", "Dog"),
            ("MobBee", "Bee"),
            ("MobChicken", "Chicken"),
            ("MobDuckMallard", "Duck"),
            ("MobCow", "Cow"),
            ("MobGoat", "Goat"),
            ("MobSheep", "Sheep"),
            ("MobMonkey", "Monkey"),
            ("MobMouse", "Mouse"),
            ("MobPig", "Pig"),
            ("MobGiantSpider", "Arachnid"),
            ("MobXeno", "Xeno"),
            ("MobDragon", "Dragon"),
            ("MobDionaNymph", "RootSpeak"),
            ("MobSlimesPet", "Bubblish"),
        };

        foreach (var (mob, language) in cases)
        {
            var uid = await SpawnAndWait(server, entMan, coords, mob);
            Assert.That(entMan.HasComponent<LanguageSpeakerComponent>(uid), Is.True,
                $"{mob} should have a LanguageSpeakerComponent.");
            var comp = entMan.GetComponent<LanguageSpeakerComponent>(uid);
            Assert.That(comp.Languages.Keys, Does.Contain(language),
                $"{mob} should speak the {language} language.");
        }
    }

    private static async Task<EntityUid> SpawnAndWait(
        Robust.UnitTesting.IServerIntegrationInstance server,
        IEntityManager entMan,
        EntityCoordinates coords,
        string prototype)
    {
        EntityUid uid = default;
        await server.WaitPost(() => uid = entMan.SpawnEntity(new ProtoId<EntityPrototype>(prototype), coords));
        await server.WaitRunTicks(3);
        return uid;
    }
}
