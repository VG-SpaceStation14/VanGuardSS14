#nullable enable
using System.Numerics;
using Content.Client._VanGuard.Language.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._VanGuard.Language;
using Content.Shared.Input;
using Robust.Client.GameObjects;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._VanGuard.Language;

/// <summary>
///     Verifies that the OpenLanguagesMenu keybind triggers the language menu.
/// </summary>
[TestFixture]
public sealed class LanguageMenuKeybindTest : InteractionTest
{
    protected override string PlayerPrototype => "InteractionTestMob";

    [SetUp]
    public override async Task Setup()
    {
        await base.Setup();

        // Give the test player a language component with at least one known language,
        // so the menu can actually be populated and opened.
        await Server.WaitPost(() =>
        {
            var uid = SEntMan.GetEntity(Player);
            var comp = SEntMan.EnsureComponent<LanguageSpeakerComponent>(uid);
            comp.Languages["GalacticCommon"] = LanguageKnowledge.Speak;
            comp.CurrentLanguage = "GalacticCommon";
        });

        // Wait for the component state to propagate to the client.
        for (var i = 0; i < 20; i++)
        {
            await RunTicks(1);
            var serverUid = SEntMan.GetEntity(Player);
            if (SEntMan.HasComponent<LanguageSpeakerComponent>(serverUid)
                && CEntMan.HasComponent<LanguageSpeakerComponent>(CEntMan.GetEntity(Player)))
                break;
        }

        // Make the player use the "human" input context, exactly like a real humanoid,
        // so the OpenLanguagesMenu keybind passes the context check in the InputManager.
        var cUid = CEntMan.GetEntity(Player);
        var inputSys = CEntMan.System<Robust.Client.GameObjects.InputSystem>();
        await Client.WaitPost(() =>
        {
            if (!CEntMan.HasComponent<InputComponent>(cUid))
                CEntMan.AddComponent<InputComponent>(cUid).ContextName = "human";
            inputSys.SetEntityContextActive();
        });
        await RunTicks(2);
    }

    [Test]
    public async Task Keybind_OpensLanguageMenu()
    {
        // Sanity check: the client knows about the component + language.
        var serverUid = SEntMan.GetEntity(Player);
        Assert.That(SEntMan.HasComponent<LanguageSpeakerComponent>(serverUid), Is.True);

        var cUid = CEntMan.GetEntity(Player);
        Assert.That(CEntMan.HasComponent<LanguageSpeakerComponent>(cUid), Is.True);
        var langSys = CEntMan.System<SharedLanguageSystem>();
        Assert.That(langSys.RetrieveKnownLanguages(cUid, LanguageKnowledge.Understand, out var langs, out _));
        Assert.That(langs.Count, Is.GreaterThan(0));

        // Press L through the real input pipeline (InputManager -> context check -> simulation).
        var inputMan = Client.ResolveDependency<IInputManager>();
        await PressL(inputMan);
        await RunTicks(2);

        var uiMan = Client.ResolveDependency<IUserInterfaceManager>();
        var window = FindWindow(uiMan);
        Assert.That(window, Is.Not.Null,
            "Pressing the L key did not open the language menu window.");
    }

    [Test]
    public async Task Window_RemembersPosition()
    {
        var inputMan = Client.ResolveDependency<IInputManager>();
        var uiMan = Client.ResolveDependency<IUserInterfaceManager>();

        // Open the window with L.
        await PressL(inputMan);
        await RunTicks(2);

        var window = FindWindow(uiMan);
        Assert.That(window, Is.Not.Null);

        // "Drag" the window to a specific position.
        var target = new Vector2(120, 160);
        await Client.WaitPost(() => LayoutContainer.SetPosition(window, target));
        await RunTicks(2);

        // Close with L.
        await PressL(inputMan);
        await RunTicks(2);
        Assert.That(FindWindow(uiMan), Is.Null, "Window should be closed after toggling L again.");

        // Reopen with L.
        await PressL(inputMan);
        await RunTicks(2);

        var reopened = FindWindow(uiMan);
        Assert.That(reopened, Is.Not.Null);
        Assert.That(reopened.Position, Is.EqualTo(target),
            "Language menu window should remember its last position when reopened.");
    }

    private async Task PressL(IInputManager inputMan)
    {
        // The input state machine deduplicates repeated Down/Up, so every press
        // needs a matching key-up before the next press.
        await Client.WaitPost(() =>
            inputMan.KeyDown(new KeyEventArgs(Keyboard.Key.L, false, false, false, false, false, 0)));
        await Client.WaitPost(() =>
            inputMan.KeyUp(new KeyEventArgs(Keyboard.Key.L, false, false, false, false, false, 0)));
    }

    private static LanguageMenuWindow? FindWindow(IUserInterfaceManager uiMan)
    {
        foreach (var child in uiMan.WindowRoot.Children)
        {
            if (FindRecursive(child) is { } found)
                return found;
        }

        return null;
    }

    private static LanguageMenuWindow? FindRecursive(Control control)
    {
        if (control is LanguageMenuWindow win)
            return win;

        foreach (var child in control.Children)
        {
            if (FindRecursive(child) is { } found)
                return found;
        }

        return null;
    }
}
