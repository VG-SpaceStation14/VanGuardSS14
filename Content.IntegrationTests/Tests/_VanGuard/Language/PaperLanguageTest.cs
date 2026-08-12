#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared._VanGuard.Language;
using Content.Shared.Paper;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Language;

/// <summary>
///     Verifies the server-side paper writing language handling: segments are
///     validated against the writer's knowledge and non-universal segments are
///     obfuscated for readers who do not understand the language.
/// </summary>
[TestFixture]
public sealed class PaperLanguageTest : GameTest
{
    private const string SecretText = "Секретный отчёт по унубе";

    [Test]
    public async Task Writing_KnownLanguage_ObfuscatesSegment()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        // The writer knows Canilunzt.
        var writer = await SpawnWithLanguages(server, entMan, coords, ("Canilunzt", LanguageKnowledge.Speak));
        var paper = await SpawnPaper(server, entMan, coords);

        var segments = new List<PaperComponent.PaperTextSegment> { new(SecretText, "Canilunzt") };
        PaperWritingTextEvent ev = new(writer, paper, SecretText, segments);
        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(paper, ref ev));
        await server.WaitRunTicks(2);

        Assert.That(ev.Cancelled, Is.False);
        Assert.That(ev.Segments.Count, Is.EqualTo(1));
        var segment = ev.Segments[0];
        Assert.That(segment.Language, Is.EqualTo("Canilunzt"));
        Assert.That(segment.Text, Is.EqualTo(SecretText));
        Assert.That(segment.ObfuscatedText, Is.Not.Empty);
        Assert.That(segment.ObfuscatedText, Is.Not.EqualTo(SecretText),
            "Text written in a non-universal language must be obfuscated for readers who don't know it.");
    }

    [Test]
    public async Task Writing_UnknownLanguage_FallsBackToUniversal()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        // The writer does not know Canilunzt.
        var writer = await SpawnWithLanguages(server, entMan, coords, ("GalacticCommon", LanguageKnowledge.Speak));
        var paper = await SpawnPaper(server, entMan, coords);

        var segments = new List<PaperComponent.PaperTextSegment> { new(SecretText, "Canilunzt") };
        PaperWritingTextEvent ev = new(writer, paper, SecretText, segments);
        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(paper, ref ev));
        await server.WaitRunTicks(2);

        Assert.That(ev.Segments.Count, Is.EqualTo(1));
        var segment = ev.Segments[0];
        Assert.That(segment.Language, Is.EqualTo(SharedLanguageSystem.CommonLanguageId),
            "A writer who cannot speak the language must have their text fall back to the common tongue.");
        Assert.That(segment.Text, Is.EqualTo(SecretText));
    }

    [Test]
    public async Task Writing_MismatchedSegments_FallsBackToUniversal()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        var writer = await SpawnWithLanguages(server, entMan, coords, ("Canilunzt", LanguageKnowledge.Speak));
        var paper = await SpawnPaper(server, entMan, coords);

        // Segment texts do not add up to the full message -> server must discard them.
        var segments = new List<PaperComponent.PaperTextSegment> { new("другое", "Canilunzt") };
        PaperWritingTextEvent ev = new(writer, paper, SecretText, segments);
        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(paper, ref ev));
        await server.WaitRunTicks(2);

        Assert.That(ev.Segments.Count, Is.EqualTo(1));
        var segment = ev.Segments[0];
        Assert.That(segment.Language, Is.EqualTo(SharedLanguageSystem.CommonLanguageId));
        Assert.That(segment.Text, Is.EqualTo(SecretText));
    }

    [Test]
    public async Task Writing_FullFlow_PreservesLanguageSegments()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        var writer = await SpawnWithLanguages(server, entMan, coords, ("Canilunzt", LanguageKnowledge.Speak));
        var paper = await SpawnPaper(server, entMan, coords);

        // Simulate the real client message: text written in Canilunzt by an author who speaks it.
        var segments = new List<PaperComponent.PaperTextSegment> { new(SecretText, "Canilunzt") };
        PaperComponent.PaperInputTextMessage msg = new(SecretText, segments);
        msg.Actor = writer;

        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(paper, msg));
        await server.WaitRunTicks(2);

        var paperComp = entMan.GetComponent<PaperComponent>(paper);
        Assert.That(paperComp.Content, Is.EqualTo(SecretText));

        Assert.That(paperComp.LanguageSegments.Count, Is.EqualTo(1),
            "Language segments must survive storing the text on the paper and not be replaced by a Universal segment.");
        var stored = paperComp.LanguageSegments[0];
        Assert.That(stored.Language, Is.EqualTo("Canilunzt"));
        Assert.That(stored.Text, Is.EqualTo(SecretText));
        Assert.That(stored.ObfuscatedText, Is.Not.Empty);
        Assert.That(stored.ObfuscatedText, Is.Not.EqualTo(SecretText),
            "The stored obfuscated text must differ from the real text, so readers who do not understand Canilunzt see garbled text.");
    }

    [Test]
    public async Task Writing_MixedLanguages_PreservesBothSegments()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        // The writer speaks both languages used on the paper.
        var writer = await SpawnWithLanguages(server, entMan, coords,
            ("Canilunzt", LanguageKnowledge.Speak),
            ("Draconic", LanguageKnowledge.Speak));
        var paper = await SpawnPaper(server, entMan, coords);

        const string vulpPart = "Лапы в порядке";
        const string draconicPart = "хвост цел";
        var text = vulpPart + draconicPart;
        var segments = new List<PaperComponent.PaperTextSegment>
        {
            new(vulpPart, "Canilunzt"),
            new(draconicPart, "Draconic"),
        };
        PaperComponent.PaperInputTextMessage msg = new(text, segments);
        msg.Actor = writer;

        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(paper, msg));
        await server.WaitRunTicks(2);

        var paperComp = entMan.GetComponent<PaperComponent>(paper);
        Assert.That(paperComp.Content, Is.EqualTo(text));
        Assert.That(paperComp.LanguageSegments.Count, Is.EqualTo(2),
            "A paper written in two different languages must keep both language segments.");

        Assert.That(paperComp.LanguageSegments[0].Language, Is.EqualTo("Canilunzt"));
        Assert.That(paperComp.LanguageSegments[0].Text, Is.EqualTo(vulpPart));
        Assert.That(paperComp.LanguageSegments[0].ObfuscatedText, Is.Not.EqualTo(vulpPart));

        Assert.That(paperComp.LanguageSegments[1].Language, Is.EqualTo("Draconic"));
        Assert.That(paperComp.LanguageSegments[1].Text, Is.EqualTo(draconicPart));
        Assert.That(paperComp.LanguageSegments[1].ObfuscatedText, Is.Not.EqualTo(draconicPart));
    }

    [Test]
    public async Task Writing_SecondAuthor_PreservesUnknownFirstLanguage()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        // Writer A knows Canilunzt and writes the first word.
        var writerA = await SpawnWithLanguages(server, entMan, coords, ("Canilunzt", LanguageKnowledge.Speak));
        var paper = await SpawnPaper(server, entMan, coords);

        PaperComponent.PaperInputTextMessage firstMsg = new("секрет",
            new List<PaperComponent.PaperTextSegment> { new("секрет", "Canilunzt") });
        firstMsg.Actor = writerA;
        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(paper, firstMsg));
        await server.WaitRunTicks(2);

        // Writer B does not know Canilunzt and appends a word in Draconic.
        var writerB = await SpawnWithLanguages(server, entMan, coords,
            ("GalacticCommon", LanguageKnowledge.Speak),
            ("Draconic", LanguageKnowledge.Speak));

        PaperComponent.PaperInputTextMessage secondMsg = new("секрет новое",
            new List<PaperComponent.PaperTextSegment>
            {
                new("секрет", "Canilunzt"),
                new(" новое", "Draconic"),
            });
        secondMsg.Actor = writerB;
        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(paper, secondMsg));
        await server.WaitRunTicks(2);

        var paperComp = entMan.GetComponent<PaperComponent>(paper);
        Assert.That(paperComp.LanguageSegments.Count, Is.EqualTo(2));
        Assert.That(paperComp.LanguageSegments[0].Language, Is.EqualTo("Canilunzt"),
            "A pre-existing Canilunzt segment must not be downgraded just because the second author cannot speak Canilunzt.");
        Assert.That(paperComp.LanguageSegments[0].ObfuscatedText, Is.Not.EqualTo("секрет"));
        Assert.That(paperComp.LanguageSegments[1].Language, Is.EqualTo("Draconic"));
    }

    [Test]
    public async Task Writing_SecondAuthor_WhitespaceExtension_PreservesFirstLanguage()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        var writerA = await SpawnWithLanguages(server, entMan, coords, ("Canilunzt", LanguageKnowledge.Speak));
        var paper = await SpawnPaper(server, entMan, coords);

        PaperComponent.PaperInputTextMessage firstMsg = new("секрет",
            new List<PaperComponent.PaperTextSegment> { new("секрет", "Canilunzt") });
        firstMsg.Actor = writerA;
        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(paper, firstMsg));
        await server.WaitRunTicks(2);

        // Writer B does not know Canilunzt. The client may attach the separator
        // space to the preserved first segment when the author presses space
        // before switching language.
        var writerB = await SpawnWithLanguages(server, entMan, coords,
            ("GalacticCommon", LanguageKnowledge.Speak),
            ("Draconic", LanguageKnowledge.Speak));

        PaperComponent.PaperInputTextMessage secondMsg = new("секрет новое",
            new List<PaperComponent.PaperTextSegment>
            {
                new("секрет ", "Canilunzt"),
                new("новое", "Draconic"),
            });
        secondMsg.Actor = writerB;
        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(paper, secondMsg));
        await server.WaitRunTicks(2);

        var paperComp = entMan.GetComponent<PaperComponent>(paper);
        Assert.That(paperComp.LanguageSegments.Count, Is.EqualTo(2));
        Assert.That(paperComp.LanguageSegments[0].Language, Is.EqualTo("Canilunzt"),
            "The preserved Canilunzt segment must not be downgraded when a trailing space is attached.");
        Assert.That(paperComp.LanguageSegments[0].ObfuscatedText, Is.Not.EqualTo("секрет "));
        Assert.That(paperComp.LanguageSegments[1].Language, Is.EqualTo("Draconic"));
    }

    [Test]
    public async Task Writing_SecondAuthor_SplitSegmentTail_PreservesLanguage()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        var writerA = await SpawnWithLanguages(server, entMan, coords, ("Canilunzt", LanguageKnowledge.Speak));
        var paper = await SpawnPaper(server, entMan, coords);

        // One multi-word Canilunzt segment.
        PaperComponent.PaperInputTextMessage firstMsg = new("секрет секрет2",
            new List<PaperComponent.PaperTextSegment> { new("секрет секрет2", "Canilunzt") });
        firstMsg.Actor = writerA;
        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(paper, firstMsg));
        await server.WaitRunTicks(2);

        // Writer B (does not know Canilunzt) inserts a Draconic word between the two
        // words, splitting the original segment into head + inserted + tail.
        var writerB = await SpawnWithLanguages(server, entMan, coords,
            ("GalacticCommon", LanguageKnowledge.Speak),
            ("Draconic", LanguageKnowledge.Speak));

        PaperComponent.PaperInputTextMessage secondMsg = new("секрет новое секрет2",
            new List<PaperComponent.PaperTextSegment>
            {
                new("секрет ", "Canilunzt"),
                new("новое ", "Draconic"),
                new("секрет2", "Canilunzt"),
            });
        secondMsg.Actor = writerB;
        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(paper, secondMsg));
        await server.WaitRunTicks(2);

        var paperComp = entMan.GetComponent<PaperComponent>(paper);
        Assert.That(paperComp.LanguageSegments.Count, Is.EqualTo(3));
        Assert.That(paperComp.LanguageSegments[0].Language, Is.EqualTo("Canilunzt"));
        Assert.That(paperComp.LanguageSegments[1].Language, Is.EqualTo("Draconic"));
        Assert.That(paperComp.LanguageSegments[2].Language, Is.EqualTo("Canilunzt"),
            "The tail of a split Canilunzt segment must keep its language.");
        Assert.That(paperComp.LanguageSegments[2].ObfuscatedText, Is.Not.EqualTo("секрет2"));
    }

    private static async Task<EntityUid> SpawnWithLanguages(
        Robust.UnitTesting.IServerIntegrationInstance server,
        IEntityManager entMan,
        EntityCoordinates coords,
        params (string Language, LanguageKnowledge Knowledge)[] languages)
    {
        EntityUid uid = default;
        await server.WaitPost(() =>
        {
            uid = entMan.SpawnEntity(new ProtoId<EntityPrototype>("BigBox"), coords);
            var comp = entMan.AddComponent<LanguageSpeakerComponent>(uid);
            foreach (var (language, knowledge) in languages)
                comp.Languages[language] = knowledge;
        });
        await server.WaitRunTicks(3);
        return uid;
    }

    private static async Task<EntityUid> SpawnPaper(
        Robust.UnitTesting.IServerIntegrationInstance server,
        IEntityManager entMan,
        EntityCoordinates coords)
    {
        EntityUid uid = default;
        await server.WaitPost(() => uid = entMan.SpawnEntity(new ProtoId<EntityPrototype>("Paper"), coords));
        await server.WaitRunTicks(3);
        return uid;
    }
}
