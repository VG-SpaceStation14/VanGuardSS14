#nullable enable
using System.Collections.Generic;
using System.Reflection;
using Content.Client.Paper.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._VanGuard.Language;
using Content.Shared.Paper;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VanGuard.Language;

/// <summary>
///     Verifies that opening a paper for editing defaults the writing language to
///     one the writer can actually speak, instead of the last segment's language.
/// </summary>
[TestFixture]
public sealed class PaperLanguageSelectedLanguageTest : InteractionTest
{
    protected override string PlayerPrototype => "InteractionTestMob";

    [SetUp]
    public override async Task Setup()
    {
        await base.Setup();

        // The player knows only the common tongue, not Canilunzt.
        await Server.WaitPost(() =>
        {
            var uid = SEntMan.GetEntity(Player);
            var comp = SEntMan.EnsureComponent<LanguageSpeakerComponent>(uid);
            comp.Languages["GalacticCommon"] = LanguageKnowledge.Speak;
            comp.CurrentLanguage = "GalacticCommon";
        });

        for (var i = 0; i < 20; i++)
        {
            await RunTicks(1);
            var suid = SEntMan.GetEntity(Player);
            if (SEntMan.HasComponent<LanguageSpeakerComponent>(suid)
                && CEntMan.HasComponent<LanguageSpeakerComponent>(CEntMan.GetEntity(Player)))
                break;
        }
    }

    [Test]
    public async Task LastSegmentLanguage_UnknownToWriter_FallsBackToCommon()
    {
        PaperWindow window = null!;
        await Client.WaitPost(() => window = new PaperWindow());

        var state = new PaperComponent.PaperBoundUserInterfaceState(
            "слово1",
            new List<PaperComponent.PaperTextSegment> { new("слово1", "Canilunzt", "бла1") },
            new List<StampDisplayInfo>(),
            PaperComponent.PaperAction.Write);

        await Client.WaitPost(() => window.Populate(state));

        var selected = (string)typeof(PaperWindow)
            .GetField("_selectedLanguage", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(window)!;

        Assert.That(selected, Is.EqualTo(SharedLanguageSystem.CommonLanguageId),
            "Newly typed text must default to the common tongue when the writer cannot speak the last segment's language.");
    }

    [Test]
    public async Task LastSegmentLanguage_KnownToWriter_IsKept()
    {
        // Give the player Canilunzt directly on the client (and server).
        await Client.WaitPost(() =>
        {
            var cUid = CEntMan.GetEntity(Player);
            var comp = CEntMan.EnsureComponent<LanguageSpeakerComponent>(cUid);
            comp.Languages["Canilunzt"] = LanguageKnowledge.Speak;
        });
        await Server.WaitPost(() =>
        {
            var sUid = SEntMan.GetEntity(Player);
            var comp = SEntMan.EnsureComponent<LanguageSpeakerComponent>(sUid);
            comp.Languages["Canilunzt"] = LanguageKnowledge.Speak;
        });
        await RunTicks(2);

        PaperWindow window = null!;
        await Client.WaitPost(() => window = new PaperWindow());

        var state = new PaperComponent.PaperBoundUserInterfaceState(
            "слово1",
            new List<PaperComponent.PaperTextSegment> { new("слово1", "Canilunzt", "бла1") },
            new List<StampDisplayInfo>(),
            PaperComponent.PaperAction.Write);

        await Client.WaitPost(() => window.Populate(state));

        var selected = (string)typeof(PaperWindow)
            .GetField("_selectedLanguage", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(window)!;

        Assert.That(selected, Is.EqualTo("Canilunzt"),
            "A writer who can speak the last segment's language keeps writing in it.");
    }
}
