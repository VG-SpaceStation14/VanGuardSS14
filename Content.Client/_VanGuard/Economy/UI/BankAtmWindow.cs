using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._VanGuard.Economy;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._VanGuard.Economy.UI;

public sealed class BankAtmWindow : FancyWindow
{
    public event Action<BankTransactionRequest>? OnRequest;
    public event Action<string>? OnAuthorize;
    public event Action? OnEject;
    public event Action? OnEndSession;

    private readonly Label _statusLabel;
    private readonly Label _cardLabel;
    private readonly LineEdit _pinInput;
    private readonly Button _authorizeButton;
    private readonly Button _ejectButton;
    private readonly Button _endButton;
    private readonly BoxContainer _authPanel;
    private readonly TabContainer _tabs;
    private readonly BankCardDisplay _cardDisplay;
    private readonly BankAccountControl _accountControl;
    private bool _authorized;
    private bool _hasCard;

    public BankAtmWindow()
    {
        Title = Loc.GetString("bank-atm-window-title");
        MinSize = new Vector2(460, 600);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Margin = new Thickness(10),
        };
        ContentsContainer.AddChild(root);

        var header = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#101820"),
                BorderColor = Color.FromHex("#4ad9ff"),
                BorderThickness = new Thickness(0, 0, 0, 2),
                ContentMarginLeftOverride = 10,
                ContentMarginTopOverride = 8,
                ContentMarginBottomOverride = 8,
            },
        };
        var headerBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
        };
        headerBox.AddChild(new Label
        {
            Text = Loc.GetString("bank-atm-header"),
            StyleClasses = { "LabelHeading" },
            FontColorOverride = Color.FromHex("#F2F4F8"),
        });
        headerBox.AddChild(new Label
        {
            Text = Loc.GetString("bank-atm-subtitle"),
            StyleClasses = { "LabelSubText" },
            FontColorOverride = Color.FromHex("#9AA4B2"),
        });
        header.AddChild(headerBox);
        root.AddChild(header);

        _statusLabel = new Label
        {
            Text = Loc.GetString("bank-ui-status-loading"),
            StyleClasses = { "LabelSubText" },
            FontColorOverride = Color.FromHex("#9AA4B2"),
        };
        _cardLabel = new Label { ClipText = true };
        _pinInput = new LineEdit { HorizontalExpand = true, IsValid = text => text.Length <= 4 };
        _authorizeButton = new Button { Text = Loc.GetString("bank-atm-authorize") };
        _ejectButton = new Button { Text = Loc.GetString("bank-atm-eject") };
        _endButton = new Button { Text = Loc.GetString("bank-atm-end-session") };

        _authorizeButton.OnPressed += _ => OnAuthorize?.Invoke(_pinInput.Text.Trim());
        _ejectButton.OnPressed += _ => OnEject?.Invoke();
        _endButton.OnPressed += _ => OnEndSession?.Invoke();

        _authPanel = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5,
        };
        _authPanel.AddChild(_cardLabel);
        _authPanel.AddChild(new Label
        {
            Text = Loc.GetString("bank-atm-pin"),
            StyleClasses = { "LabelSubText" },
        });
        _authPanel.AddChild(_pinInput);
        _authPanel.AddChild(_authorizeButton);
        root.AddChild(_statusLabel);
        root.AddChild(_authPanel);

        _tabs = new TabContainer { VerticalExpand = true, HorizontalExpand = true };
        _cardDisplay = new BankCardDisplay();
        _accountControl = new BankAccountControl();
        _accountControl.OnRequest += request => OnRequest?.Invoke(request);

        var accounts = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
        };
        accounts.AddChild(new Label
        {
            Text = Loc.GetString("bank-tab-account-description"),
            StyleClasses = { "LabelSubText" },
            FontColorOverride = Color.FromHex("#9AA4B2"),
        });
        accounts.AddChild(_cardDisplay);

        var operations = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
        };
        operations.AddChild(_accountControl);

        _tabs.AddChild(accounts);
        _tabs.AddChild(operations);
        _tabs.SetTabTitle(0, Loc.GetString("bank-tab-accounts"));
        _tabs.SetTabTitle(1, Loc.GetString("bank-tab-operations"));
        // TabContainer draws its own tabs instead of using BaseButton, so the
        // global button-click sound does not cover tab switching.
        _tabs.OnTabChanged += _ => UserInterfaceManager.ClickSound();
        root.AddChild(_tabs);

        var footer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        footer.AddChild(_ejectButton);
        footer.AddChild(_endButton);
        root.AddChild(footer);

        UpdateState(new BankAccountUiState(null, string.Empty, 0, null, BankTransactionStatus.Loading, hasCard: false, authorized: false));
    }

    public void UpdateState(BankAccountUiState state)
    {
        _authorized = state.Authorized;
        _hasCard = state.HasCard;
        _statusLabel.Text = _authorized
            ? Loc.GetString("bank-atm-status-ready")
            : Loc.GetString(StateKey(state.LastStatus));
        _cardLabel.Text = state.HasCard
            ? Loc.GetString("bank-atm-card-inserted", ("owner", state.CardOwnerName))
            : Loc.GetString("bank-atm-insert-card");

        _pinInput.Visible = _hasCard && !_authorized;
        _authorizeButton.Visible = _hasCard && !_authorized;
        _authPanel.Visible = !_authorized;
        _tabs.Visible = _authorized;
        _ejectButton.Visible = _hasCard;
        _endButton.Visible = _authorized;
        _accountControl.Visible = _authorized;

        if (_authorized)
            _pinInput.SetText(string.Empty, invokeEvent: false);

        _cardDisplay.UpdateState(state);
        if (_authorized)
            _accountControl.UpdateState(state);
    }

    private static string StateKey(BankTransactionStatus status)
    {
        return status switch
        {
            BankTransactionStatus.Success => "bank-ui-status-success",
            BankTransactionStatus.InvalidAmount => "bank-ui-status-invalid-amount",
            BankTransactionStatus.InsufficientFunds => "bank-ui-status-insufficient-funds",
            BankTransactionStatus.NoAccount => "bank-ui-status-no-account",
            BankTransactionStatus.NoCash => "bank-ui-status-no-cash",
            BankTransactionStatus.CashTooLarge => "bank-ui-status-cash-too-large",
            BankTransactionStatus.WrongPin => "bank-atm-status-wrong-pin",
            BankTransactionStatus.NotAuthorized => "bank-atm-status-not-authorized",
            BankTransactionStatus.NoCard => "bank-atm-status-no-card",
            BankTransactionStatus.CardMismatch => "bank-atm-status-card-mismatch",
            BankTransactionStatus.DuplicateRequest => "bank-ui-status-duplicate",
            BankTransactionStatus.InvalidRequest => "bank-atm-status-enter-pin",
            BankTransactionStatus.Loading => "bank-ui-status-loading",
            _ => "bank-atm-status-insert-card",
        };
    }
}
