using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._VanGuard.Language;
using Content.Shared.Input;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Input.Binding;

namespace Content.Client._VanGuard.Language.UI;

[UsedImplicitly]
public sealed partial class LanguageUIController : UIController, IOnStateChanged<GameplayState>
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IEntityManager _entMan = default!;

    private LanguageMenuWindow? _menu;

    private MenuButton? LanguagesButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.LanguagesButton;

    public override void Initialize()
    {
        EntityManager.EventBus.SubscribeEvent<LanguageMenuStateMessage>(EventSource.All, this, OnStateUpdate);
    }

    public void OnStateEntered(GameplayState state)
    {
        // The window is created once per gameplay session and reused across open/close
        // cycles so that it remembers its position on screen, like other menu windows.
        _menu = UIManager.CreateWindow<LanguageMenuWindow>();
        LayoutContainer.SetAnchorPreset(_menu, LayoutContainer.LayoutPreset.CenterTop);
        _menu.OnClose += OnWindowClosed;
        _menu.OnLanguageSelected += OnLanguageSelected;

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenLanguagesMenu,
                InputCmdHandler.FromDelegate(_ => ToggleLanguagesMenu()))
            .Register<LanguageUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        if (_menu != null)
        {
            _menu.OnClose -= OnWindowClosed;
            _menu.OnLanguageSelected -= OnLanguageSelected;
            _menu.Close();
            _menu = null;
        }

        CommandBinds.Unregister<LanguageUIController>();
    }

    public void LoadButton()
    {
        if (LanguagesButton != null)
            LanguagesButton.OnPressed += OnLanguagesButtonPressed;
    }

    public void UnloadButton()
    {
        if (LanguagesButton != null)
            LanguagesButton.OnPressed -= OnLanguagesButtonPressed;
    }

    private void OnLanguagesButtonPressed(BaseButton.ButtonEventArgs args)
    {
        ToggleLanguagesMenu();
    }

    private void OnStateUpdate(LanguageMenuStateMessage msg)
    {
        if (_menu == null)
            return;

        if (!_player.LocalEntity.HasValue || msg.ComponentOwner != _entMan.GetNetEntity(_player.LocalEntity.Value))
            return;

        _menu.UpdateState(msg.CurrentLanguage, msg.Options, msg.TranslatorOptions);
    }

    private void ToggleLanguagesMenu()
    {
        var player = _player.LocalEntity;
        if (!player.HasValue)
            return;

        if (_menu == null)
            return;

        if (_menu.IsOpen)
        {
            CloseMenu();
            return;
        }

        var language = _entMan.System<LanguageSystem>();
        if (!language.RetrieveKnownLanguages(player.Value, LanguageKnowledge.Understand, out var langs, out var translator))
            return;

        _menu.UpdateState(language.GetSelectedLanguage(player.Value).ID, langs, translator);

        if (LanguagesButton != null)
            LanguagesButton.SetClickPressed(true);

        _menu.Open();
    }

    private void CloseMenu()
    {
        if (_menu == null)
            return;

        _menu.Close();

        if (LanguagesButton != null)
            LanguagesButton.SetClickPressed(false);
    }

    private void OnWindowClosed()
    {
        if (LanguagesButton != null)
            LanguagesButton.SetClickPressed(false);
    }

    private void OnLanguageSelected(string languageId)
    {
        _entMan.System<LanguageSystem>().RequestLanguageSwitch(languageId);
    }
}


