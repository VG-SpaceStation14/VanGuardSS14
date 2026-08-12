#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client.Paper.UI;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Paper;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._VanGuard.Language;

/// <summary>
///     Verifies the client-side language segment rebuilding for mixed-language
///     documents. Drives the private PaperWindow helpers directly to reproduce
///     the exact state the editor is in while a writer appends words in
///     different languages on the same line.
/// </summary>
[TestFixture]
public sealed class PaperLanguageClientTest : GameTest
{
    [Test]
    public async Task AppendSecondWord_AuthorKnowsBoth_PreservesBothSegments()
    {
        var window = await CreateWindow();
        SetOriginal(window, "слово1", "Canilunzt", "крхкрх");
        SetMarkers(window, (6, "Draconic"));
        SetLanguageState(window, "Canilunzt", "Draconic", languageSelectionChanged: true);

        var segments = BuildSegments(window, "слово1 слово2");

        Assert.That(segments.Count, Is.EqualTo(2));
        Assert.That(segments[0].Language, Is.EqualTo("Canilunzt"));
        Assert.That(segments[0].Text, Is.EqualTo("слово1"));
        Assert.That(segments[1].Language, Is.EqualTo("Draconic"));
        Assert.That(segments[1].Text, Is.EqualTo(" слово2"));
        Assert.That(Concat(segments), Is.EqualTo("слово1 слово2"));
    }

    [Test]
    public async Task AppendSecondWord_AuthorDoesNotUnderstandFirst_RebuildsRawText()
    {
        var window = await CreateWindow();
        // The first word is shown obfuscated ("крхкрх") to this writer.
        SetOriginal(window, "секрет", "Canilunzt", "крхкрх", displayText: "крхкрх");
        SetMarkers(window, (6, "Draconic"));
        SetLanguageState(window, "Canilunzt", "Draconic", languageSelectionChanged: true);

        var segments = BuildSegments(window, "крхкрх новое");

        Assert.That(segments.Count, Is.EqualTo(2));
        Assert.That(segments[0].Language, Is.EqualTo("Canilunzt"));
        Assert.That(segments[0].Text, Is.EqualTo("секрет"),
            "The raw first word must be kept, not the obfuscated display text.");
        Assert.That(segments[1].Language, Is.EqualTo("Draconic"));
        Assert.That(segments[1].Text, Is.EqualTo(" новое"));
        // RunOnSaved derives the server text from the segments.
        Assert.That(Concat(segments), Is.EqualTo("секрет новое"));
    }
    [Test]
    public async Task AppendThirdWord_ThreeLanguagesOnOneLine()
    {
        var window = await CreateWindow();
        // The paper already has two segments from a previous save.
        SetField(window, "_originalLanguageSegments", new List<PaperComponent.PaperTextSegment>
        {
            new("слово1", "Canilunzt", "блб"),
            new(" слово2", "Draconic", "кркр"),
        });
        SetField(window, "_originalDisplaySegments", new List<PaperComponent.PaperTextSegment>
        {
            new("слово1", "Canilunzt"),
            new(" слово2", "Draconic"),
        });
        SetField(window, "_originalEditableText", "слово1 слово2");
        SetMarkers(window, (6, "Draconic"), (13, "Canilunzt"));
        SetLanguageState(window, "Canilunzt", "Canilunzt", languageSelectionChanged: true);

        var segments = BuildSegments(window, "слово1 слово2 слово3");

        Assert.That(segments.Count, Is.EqualTo(3));
        Assert.That(segments[0].Language, Is.EqualTo("Canilunzt"));
        Assert.That(segments[0].Text, Is.EqualTo("слово1"));
        Assert.That(segments[1].Language, Is.EqualTo("Draconic"));
        Assert.That(segments[1].Text, Is.EqualTo(" слово2"));
        Assert.That(segments[2].Language, Is.EqualTo("Canilunzt"));
        Assert.That(segments[2].Text, Is.EqualTo(" слово3"));
        Assert.That(Concat(segments), Is.EqualTo("слово1 слово2 слово3"));
    }

    [Test]
    public async Task AppendToChain_AuthorDoesNotUnderstandMiddle_PreservesAllSegments()
    {
        var window = await CreateWindow();
        // Paper: Canilunzt + GalacticCommon + Canilunzt + GalacticCommon.
        SetField(window, "_originalLanguageSegments", new List<PaperComponent.PaperTextSegment>
        {
            new("слово1", "Canilunzt", "бла1"),
            new(" слово2", "GalacticCommon", ""),
            new(" слово3", "Canilunzt", "бла3"),
            new(" слово4", "GalacticCommon", ""),
        });
        // The second author does not understand Canilunzt -> those parts are obfuscated.
        SetField(window, "_originalDisplaySegments", new List<PaperComponent.PaperTextSegment>
        {
            new("бла1", "Canilunzt"),
            new(" слово2", "GalacticCommon"),
            new(" бла3", "Canilunzt"),
            new(" слово4", "GalacticCommon"),
        });
        SetField(window, "_originalEditableText", "бла1 слово2 бла3 слово4");
        // The author picked Sinta'Unati before appending the new word.
        SetLanguageState(window, "Canilunzt", "Sinta'Unati", languageSelectionChanged: true);

        var segments = BuildSegments(window, "бла1 слово2 бла3 слово4 слово5");

        Assert.That(segments.Count, Is.EqualTo(5));
        Assert.That(segments[0].Language, Is.EqualTo("Canilunzt"));
        Assert.That(segments[0].Text, Is.EqualTo("слово1"));
        Assert.That(segments[2].Language, Is.EqualTo("Canilunzt"),
            "The middle Canilunzt segment must keep its language and raw text.");
        Assert.That(segments[2].Text, Is.EqualTo(" слово3"));
        Assert.That(segments[4].Language, Is.EqualTo("Sinta'Unati"));
        Assert.That(segments[4].Text, Is.EqualTo(" слово5"));
        Assert.That(Concat(segments), Is.EqualTo("слово1 слово2 слово3 слово4 слово5"));
    }

    [Test]
    public async Task InsertMiddle_ThenAppend_DoesNotReTagWholeLine()
    {
        var window = await CreateWindow();
        // Paper with mixed segments; the author understands all of them.
        SetField(window, "_originalLanguageSegments", new List<PaperComponent.PaperTextSegment>
        {
            new("слово1", "Canilunzt", "бл1"),
            new(" слово2", "GalacticCommon", ""),
            new(" слово3", "Canilunzt", "бл3"),
            new(" слово4", "GalacticCommon", ""),
        });
        SetField(window, "_originalDisplaySegments", new List<PaperComponent.PaperTextSegment>
        {
            new("слово1", "Canilunzt"),
            new(" слово2", "GalacticCommon"),
            new(" слово3", "Canilunzt"),
            new(" слово4", "GalacticCommon"),
        });
        SetField(window, "_originalEditableText", "слово1 слово2 слово3 слово4");
        SetMarkers(window, (6, "GalacticCommon"), (13, "Canilunzt"), (20, "GalacticCommon"));
        // The author picked Draconic from the dropdown before inserting.
        SetLanguageState(window, "Canilunzt", "Draconic", languageSelectionChanged: true);

        // The author inserts " X" (Draconic) in the middle, then appends " слово5".
        var segments = BuildSegments(window, "слово1 слово2 X слово3 слово4 слово5");
        var summary = string.Join(" | ", segments.Select(s => $"{s.Language}:{s.Text}"));

        Assert.That(summary, Is.EqualTo(
            "Canilunzt:слово1 | GalacticCommon: слово2 | Draconic: X | Canilunzt: слово3 | GalacticCommon: слово4 | Draconic: слово5"),
            "Inserting in the middle must only re-tag the inserted text, not the whole line.");
    }

    [Test]
    public async Task EditMiddle_WithObfuscatedSegment_DoesNotLoseSegments()
    {
        var window = await CreateWindow();
        SetField(window, "_originalLanguageSegments", new List<PaperComponent.PaperTextSegment>
        {
            new("слово1", "Canilunzt", "бла1"),
            new(" слово2", "GalacticCommon", ""),
            new(" слово3", "Canilunzt", "бла3"),
            new(" слово4", "GalacticCommon", ""),
        });
        SetField(window, "_originalDisplaySegments", new List<PaperComponent.PaperTextSegment>
        {
            new("бла1", "Canilunzt"),
            new(" слово2", "GalacticCommon"),
            new(" бла3", "Canilunzt"),
            new(" слово4", "GalacticCommon"),
        });
        SetField(window, "_originalEditableText", "бла1 слово2 бла3 слово4");
        SetLanguageState(window, "Canilunzt", "GalacticCommon", languageSelectionChanged: false);

        // The author replaces the second word (GalacticCommon) with a new one.
        var segments = BuildSegments(window, "бла1 новое2 бла3 слово4");
        var summary = string.Join(" | ", segments.Select(s => $"{s.Language}:{s.Text}"));
        Assert.That(summary, Is.EqualTo(
            "Canilunzt:слово1 | GalacticCommon: новое2 | Canilunzt: слово3 | GalacticCommon: слово4"),
            "Middle edit must keep raw Canilunzt segments and only replace the edited word.");
    }

    [Test]
    public async Task TypingFlow_MarkerSurvivesAppend_ProducesTwoSegments()
    {
        var window = await CreateWindow();
        SetOriginal(window, "слово1", "Canilunzt", "крхкрх");
        SetLanguageState(window, "Canilunzt", "Canilunzt", languageSelectionChanged: false);

        await Client.WaitPost(() =>
        {
            window.Input.TextRope = Rope.Leaf.Empty;
            window.Input.InsertAtCursor("слово1");
            // Simulate the author picking another language from the dropdown while
            // the cursor sits at the end of the first word.
            SetField(window, "_selectedLanguage", "Draconic");
            SetField(window, "_languageSelectionChanged", true);
            InvokeAddLanguageMarker(window, "Draconic");
            // Type the second word right after the first one.
            window.Input.InsertAtCursor(" слово2");
        });

        var markers = GetMarkers(window);
        Assert.That(markers.Count, Is.EqualTo(1),
            "The language switch marker must survive the keystrokes of the second word.");

        var text = Rope.Collapse(window.Input.TextRope);
        var segments = BuildSegments(window, text);
        Assert.That(segments.Count, Is.EqualTo(2));
        Assert.That(segments[0].Language, Is.EqualTo("Canilunzt"));
        Assert.That(segments[0].Text, Is.EqualTo("слово1"));
        Assert.That(segments[1].Language, Is.EqualTo("Draconic"));
        Assert.That(segments[1].Text, Is.EqualTo(" слово2"));
        Assert.That(Concat(segments), Is.EqualTo("слово1 слово2"));
    }

    [Test]
    public async Task InsertBetweenTwoSameLanguageWords_SplitsSegment()
    {
        var window = await CreateWindow();
        // Two Canilunzt words stored as ONE segment.
        SetField(window, "_originalLanguageSegments", new List<PaperComponent.PaperTextSegment>
        {
            new("слово1 слово2", "Canilunzt", "бла1 бла2"),
        });
        SetField(window, "_originalDisplaySegments", new List<PaperComponent.PaperTextSegment>
        {
            new("слово1 слово2", "Canilunzt"),
        });
        SetField(window, "_originalEditableText", "слово1 слово2");
        // The author picked Draconic and inserted a word between the two Canilunzt words.
        SetLanguageState(window, "Canilunzt", "Draconic", languageSelectionChanged: true);

        var segments = BuildSegments(window, "слово1 новое слово2");

        Assert.That(segments.Count, Is.EqualTo(3));
        Assert.That(segments[0].Language, Is.EqualTo("Canilunzt"));
        Assert.That(segments[0].Text, Is.EqualTo("слово1"),
            "The first Canilunzt word must stay in Canilunzt.");
        Assert.That(segments[1].Language, Is.EqualTo("Draconic"));
        Assert.That(segments[1].Text, Is.EqualTo(" новое"),
            "Only the inserted word may be in Draconic.");
        Assert.That(segments[2].Language, Is.EqualTo("Canilunzt"));
        Assert.That(segments[2].Text, Is.EqualTo(" слово2"),
            "The second Canilunzt word must stay in Canilunzt.");
        Assert.That(Concat(segments), Is.EqualTo("слово1 новое слово2"));
    }

    [Test]
    public async Task PressSpaceBeforeSwitching_AttachesSpaceToNewSegment()
    {
        var window = await CreateWindow();
        // The first word is shown obfuscated to this writer.
        SetOriginal(window, "слово1", "Canilunzt", "бла1", displayText: "бла1");
        SetLanguageState(window, "Canilunzt", "Canilunzt", languageSelectionChanged: false);

        await Client.WaitPost(() =>
        {
            window.Input.TextRope = Rope.Leaf.Empty;
            window.Input.InsertAtCursor("бла1");
            // Press a space (the "отступ"), then pick Draconic, then type the word.
            window.Input.InsertAtCursor(" ");
            SetField(window, "_selectedLanguage", "Draconic");
            SetField(window, "_languageSelectionChanged", true);
            InvokeAddLanguageMarker(window, "Draconic");
            window.Input.InsertAtCursor("новое");
        });

        var text = Rope.Collapse(window.Input.TextRope);
        var segments = BuildSegments(window, text);

        // The preserved first segment must keep its exact raw text without the
        // separator space, and the space must belong to the new Draconic segment.
        Assert.That(segments.Count, Is.EqualTo(2));
        Assert.That(segments[0].Language, Is.EqualTo("Canilunzt"));
        Assert.That(segments[0].Text, Is.EqualTo("слово1"),
            "The separator space must not be appended to the preserved Canilunzt segment.");
        Assert.That(segments[1].Language, Is.EqualTo("Draconic"));
        Assert.That(segments[1].Text, Is.EqualTo(" новое"));
        Assert.That(Concat(segments), Is.EqualTo("слово1 новое"));
    }

    [Test]
    public async Task ReplaceWord_KeepsOtherSegments()
    {
        var window = await CreateWindow();
        SetField(window, "_originalLanguageSegments", new List<PaperComponent.PaperTextSegment>
        {
            new("слово1", "Canilunzt", "бла1"),
            new(" слово2", "GalacticCommon", ""),
            new(" слово3", "Canilunzt", "бла3"),
        });
        SetField(window, "_originalDisplaySegments", new List<PaperComponent.PaperTextSegment>
        {
            new("слово1", "Canilunzt"),
            new(" слово2", "GalacticCommon"),
            new(" слово3", "Canilunzt"),
        });
        SetField(window, "_originalEditableText", "слово1 слово2 слово3");
        SetLanguageState(window, "Canilunzt", "Draconic", languageSelectionChanged: true);

        var segments = BuildSegments(window, "слово1 другое слово3");
        var summary = string.Join(" | ", segments.Select(s => $"{s.Language}:{s.Text}"));
        Assert.That(summary, Is.EqualTo(
            "Canilunzt:слово1 | Draconic: другое | Canilunzt: слово3"));
    }

    [Test]
    public async Task DeleteWordFromMiddle_KeepsOthers()
    {
        var window = await CreateWindow();
        SetField(window, "_originalLanguageSegments", new List<PaperComponent.PaperTextSegment>
        {
            new("слово1", "Canilunzt", "бла1"),
            new(" слово2", "GalacticCommon", ""),
            new(" слово3", "Canilunzt", "бла3"),
        });
        SetField(window, "_originalDisplaySegments", new List<PaperComponent.PaperTextSegment>
        {
            new("слово1", "Canilunzt"),
            new(" слово2", "GalacticCommon"),
            new(" слово3", "Canilunzt"),
        });
        SetField(window, "_originalEditableText", "слово1 слово2 слово3");
        SetLanguageState(window, "Canilunzt", "Canilunzt", languageSelectionChanged: false);

        var segments = BuildSegments(window, "слово1 слово3");
        Assert.That(segments.Count, Is.EqualTo(1));
        Assert.That(segments[0].Language, Is.EqualTo("Canilunzt"));
        Assert.That(segments[0].Text, Is.EqualTo("слово1 слово3"));
    }

    [Test]
    public async Task InsertWordBeforeObfuscatedWord_AuthorDoesNotKnow()
    {
        var window = await CreateWindow();
        // The writer does not know Canilunzt: the first word is obscured.
        SetField(window, "_originalLanguageSegments", new List<PaperComponent.PaperTextSegment>
        {
            new("слово1", "Canilunzt", "бла1"),
            new(" слово2", "GalacticCommon", ""),
        });
        SetField(window, "_originalDisplaySegments", new List<PaperComponent.PaperTextSegment>
        {
            new("бла1", "Canilunzt"),
            new(" слово2", "GalacticCommon"),
        });
        SetField(window, "_originalEditableText", "бла1 слово2");
        SetLanguageState(window, "Canilunzt", "Draconic", languageSelectionChanged: true);

        // A new word is typed before the obscured one.
        var segments = BuildSegments(window, "новое бла1 слово2");
        var summary = string.Join(" | ", segments.Select(s => $"{s.Language}:{s.Text}"));
        Assert.That(summary, Is.EqualTo(
            "Draconic:новое | Canilunzt: слово1 | GalacticCommon: слово2"));
    }

    [Test]
    public async Task Retag_WithObfuscatedSegment_PreservesOriginalRawSegments()
    {
        var window = await CreateWindow();
        // The writer cannot read the first word (it is shown as "бла1").
        SetOriginal(window, "слово1", "Canilunzt", "бла1", displayText: "бла1");
        // The writer changed the dropdown language but left the text untouched.
        SetLanguageState(window, "Canilunzt", "Draconic", languageSelectionChanged: true);

        var segments = BuildSegments(window, "бла1");

        // Marker-based retagging must NOT run over obscured text: the raw segment
        // (with the real word, not the obfuscated display text) is preserved instead.
        Assert.That(segments.Count, Is.EqualTo(1));
        Assert.That(segments[0].Text, Is.EqualTo("слово1"));
        Assert.That(segments[0].Language, Is.EqualTo("Canilunzt"));
    }

    private void InvokeAddLanguageMarker(PaperWindow window, string language)
    {
        var method = typeof(PaperWindow).GetMethod("AddLanguageMarker", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(window, new object[] { language });
    }

    private static List<LanguageMarkerSnapshot> GetMarkers(PaperWindow window)
    {
        var field = typeof(PaperWindow).GetField("_languageMarkers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var value = (IEnumerable)field.GetValue(window)!;
        var result = new List<LanguageMarkerSnapshot>();
        foreach (var marker in value)
        {
            var type = marker.GetType();
            var position = (int)type.GetProperty("Position")!.GetValue(marker)!;
            var language = (string)type.GetProperty("Language")!.GetValue(marker)!;
            result.Add(new LanguageMarkerSnapshot(position, language));
        }
        return result;
    }

    private readonly record struct LanguageMarkerSnapshot(int Position, string Language);

    private async Task<PaperWindow> CreateWindow()
    {
        PaperWindow window = null!;
        await Client.WaitPost(() => window = new PaperWindow());
        return window;
    }

    private void SetOriginal(
        PaperWindow window,
        string rawText,
        string language,
        string obfuscatedText,
        string? displayText = null,
        (string Text, string Language)[]? displaySegments = null)
    {
        displayText ??= rawText;
        var rawSegments = new List<PaperComponent.PaperTextSegment>
        {
            new(rawText, language, obfuscatedText),
        };
        var display = displaySegments != null
            ? displaySegments.Select(s => new PaperComponent.PaperTextSegment(s.Text, s.Language)).ToList()
            : new List<PaperComponent.PaperTextSegment> { new(displayText, language) };

        SetField(window, "_originalLanguageSegments", rawSegments);
        SetField(window, "_originalDisplaySegments", display);
        SetField(window, "_originalEditableText", displayText);
    }

    private void SetLanguageState(PaperWindow window, string initialLanguage, string selectedLanguage, bool languageSelectionChanged)
    {
        SetField(window, "_initialLanguage", initialLanguage);
        SetField(window, "_selectedLanguage", selectedLanguage);
        SetField(window, "_languageSelectionChanged", languageSelectionChanged);
    }

    private void SetMarkers(PaperWindow window, params (int Position, string Language)[] markers)
    {
        var markerType = typeof(PaperWindow).GetNestedType("LanguageMarker", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(markerType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var (position, language) in markers)
        {
            var marker = Activator.CreateInstance(markerType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new object[] { position, language }, null)!;
            list.Add(marker);
        }
        SetField(window, "_languageMarkers", list);
    }

    private List<PaperComponent.PaperTextSegment> BuildSegments(PaperWindow window, string text)
    {
        var method = typeof(PaperWindow).GetMethod("BuildLanguageSegments", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (List<PaperComponent.PaperTextSegment>)method.Invoke(window, new object[] { text })!;
    }

    private void SetField(PaperWindow window, string name, object value)
    {
        var field = typeof(PaperWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(window, value);
    }

    private static string Concat(List<PaperComponent.PaperTextSegment> segments)
    {
        return string.Concat(segments.Select(segment => segment.Text));
    }
}

