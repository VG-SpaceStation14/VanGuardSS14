#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._VanGuard.Language;
using Content.Shared._VanGuard.Language;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._VanGuard.Language;

/// <summary>
///     Server-side tests for the core language logic: knowledge gating,
///     universal speakers, default language selection and obfuscation stability.
/// </summary>
[TestFixture]
public sealed class LanguageSystemTest : GameTest
{
    [Test]
    public async Task Knowledge_GatesSpeakAndUnderstand()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        var uid = await SpawnWithLanguages(server, entMan, coords, new Dictionary<string, LanguageKnowledge>
        {
            ["GalacticCommon"] = LanguageKnowledge.Speak,
            ["Canilunzt"] = LanguageKnowledge.Understand,
        });

        var langSys = entMan.System<LanguageSystem>();
        Assert.That(langSys.CanSpeak(uid, "GalacticCommon"), Is.True);
        Assert.That(langSys.CanUnderstand(uid, "GalacticCommon"), Is.True);
        Assert.That(langSys.CanSpeak(uid, "Canilunzt"), Is.False,
            "Understand-only knowledge must not allow speaking.");
        Assert.That(langSys.CanUnderstand(uid, "Canilunzt"), Is.True);
        Assert.That(langSys.CanSpeak(uid, "SikTaj"), Is.False);
        Assert.That(langSys.CanUnderstand(uid, "SikTaj"), Is.False);
    }

    [Test]
    public async Task UniversalSpeaker_CanSpeakAndUnderstandEverything()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        EntityUid uid = default;
        await server.WaitPost(() =>
        {
            uid = entMan.SpawnEntity(new ProtoId<EntityPrototype>("BigBox"), coords);
            entMan.AddComponent<UniversalLanguageSpeakerComponent>(uid);
        });
        await server.WaitRunTicks(3);

        var langSys = entMan.System<LanguageSystem>();
        Assert.That(langSys.CanSpeak(uid, "SikTaj"), Is.True);
        Assert.That(langSys.CanUnderstand(uid, "SikTaj"), Is.True);
        Assert.That(langSys.CanSpeak(uid, "Xeno"), Is.True);
        Assert.That(langSys.CanUnderstand(uid, "Xeno"), Is.True);
    }

    [Test]
    public async Task SelectDefaultLanguage_PicksMostFluentKnown()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        // Fluent in one language, barely know another: fluency must win over priority.
        var uid = await SpawnWithLanguages(server, entMan, coords, new Dictionary<string, LanguageKnowledge>
        {
            ["GalacticCommon"] = LanguageKnowledge.BadSpeak,
            ["Canilunzt"] = LanguageKnowledge.Speak,
        });

        var langSys = entMan.System<LanguageSystem>();

        // Adding the component to an already-initialized entity fires a MapInit that
        // auto-selects before the languages dict is populated, so reset it first.
        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<LanguageSpeakerComponent>(uid);
            comp.CurrentLanguage = null;
            langSys.SelectDefaultLanguage(uid);
        });
        await server.WaitRunTicks(2);

        var comp = entMan.GetComponent<LanguageSpeakerComponent>(uid);
        Assert.That(comp.CurrentLanguage, Is.EqualTo("Canilunzt"),
            "Fluency should beat priority when picking the default language.");
    }

    [Test]
    public async Task ObfuscateMessage_IsStableWithinRound()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var random = server.ResolveDependency<IRobustRandom>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        var uid = await SpawnWithLanguages(server, entMan, coords, new Dictionary<string, LanguageKnowledge>
        {
            ["GalacticCommon"] = LanguageKnowledge.Speak,
        });

        var langSys = entMan.System<LanguageSystem>();
        var protoId = new ProtoId<LanguagePrototype>("GalacticCommon");
        var style = protoMan.Index(protoId).Style;

        string first = "";
        string second = "";
        await server.WaitPost(() =>
        {
            first = langSys.ObfuscateMessage(uid, "The cargo shuttle is docking.", style, random);
            second = langSys.ObfuscateMessage(uid, "The cargo shuttle is docking.", style, random);
        });

        Assert.That(first, Is.Not.Empty);
        Assert.That(second, Is.EqualTo(first),
            "Obfuscation must be stable for the same message inside one round.");
        Assert.That(first, Is.Not.EqualTo("The cargo shuttle is docking."),
            "A message spoken in another language should not be transmitted verbatim.");
    }

    [Test]
    public async Task AccentuateMessage_OnlyAffectsBadSpeak()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        var fluent = await SpawnWithLanguages(server, entMan, coords, new Dictionary<string, LanguageKnowledge>
        {
            ["GalacticCommon"] = LanguageKnowledge.Speak,
        });
        var poor = await SpawnWithLanguages(server, entMan, coords, new Dictionary<string, LanguageKnowledge>
        {
            ["GalacticCommon"] = LanguageKnowledge.BadSpeak,
        });

        var langSys = entMan.System<LanguageSystem>();
        const string message =
            "The cargo shuttle is docking with the station right now and the crew should prepare for it.";
        string fluentResult = "";
        string poorResult = "";
        await server.WaitPost(() =>
        {
            fluentResult = langSys.AccentuateMessage(fluent, "GalacticCommon", message);
            poorResult = langSys.AccentuateMessage(poor, "GalacticCommon", message);
        });

        Assert.That(fluentResult, Is.EqualTo(message),
            "Fluent speakers should not have their speech accentuated.");
        Assert.That(poorResult, Is.Not.EqualTo(message),
            "BadSpeak speakers should get a noticeable accent.");
    }

    private static async Task<EntityUid> SpawnWithLanguages(
        Robust.UnitTesting.IServerIntegrationInstance server,
        IEntityManager entMan,
        EntityCoordinates coords,
        Dictionary<string, LanguageKnowledge> languages)
    {
        EntityUid uid = default;
        await server.WaitPost(() =>
        {
            uid = entMan.SpawnEntity(new ProtoId<EntityPrototype>("BigBox"), coords);
            var comp = entMan.AddComponent<LanguageSpeakerComponent>(uid);
            foreach (var (id, level) in languages)
                comp.Languages[id] = level;
        });
        await server.WaitRunTicks(3);
        return uid;
    }
}

