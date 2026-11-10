using System.Globalization;
using System.Numerics;
using System.Threading;
using Content.Shared._VanGuard.Economy;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Client._VanGuard.Economy.UI;

/// <summary>
///     Read-only recipient search and transfer form for the bank cartridge.
///     All validation and account resolution is repeated by the server.
/// </summary>
public sealed class BankTransferControl : BoxContainer
{
    private const string PositiveColor = "#67E8A5";
    private const string NegativeColor = "#E06C75";
    private const string NeutralColor = "#9AA4B2";
    private const int SearchDebounceMilliseconds = 300;

    public event Action<string>? OnSearchRequested;
    public event Action<BankTransferRecipient, int, int>? OnTransferRequested;

    private readonly LineEdit _search;
    private readonly Label _searchStatus;
    private readonly BoxContainer _results;
    private readonly PanelContainer _selectedPanel;
    private readonly Label _selectedName;
    private readonly Label _selectedAccount;
    private readonly Button _changeButton;
    private readonly Label _available;
    private readonly LineEdit _amount;
    private readonly Button _sendButton;
    private readonly PanelContainer _confirmPanel;
    private readonly Label _confirmLabel;
    private readonly Button _confirmButton;
    private readonly Button _cancelButton;
    private readonly Label _statusLabel;
    private readonly BoxContainer _content;
    private readonly PanelContainer _noAccountPanel;

    private CancellationTokenSource? _searchDebounce;
    private BankTransferRecipient? _selected;
    private BankTransferRecipient? _pendingRecipient;
    private int _balance;
    private bool _hasAccount;
    private bool _awaitingResult;
    private int _pendingRequestId;
    private int _pendingAmount;
    private int _nextRequestId = Random.Shared.Next(1, int.MaxValue);
    private string _query = string.Empty;

    public BankTransferControl()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        SeparationOverride = 6;
        Margin = new Thickness(4);

        _noAccountPanel = CreateMessagePanel("bank-ui-transfer-no-account");
        _noAccountPanel.Visible = false;
        AddChild(_noAccountPanel);

        _content = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        AddChild(_content);

        _content.AddChild(new Label
        {
            Text = Loc.GetString("bank-ui-transfer-search-title"),
            StyleClasses = { "LabelHeading" },
        });

        _search = new LineEdit
        {
            PlaceHolder = Loc.GetString("bank-ui-transfer-search-placeholder"),
            HorizontalExpand = true,
        };
        _search.OnTextChanged += args => OnSearchTextChanged(args.Text);
        _search.OnTextEntered += args => StartSearch(args.Text);
        _content.AddChild(_search);

        _searchStatus = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true,
        };
        _content.AddChild(_searchStatus);

        _results = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };
        _content.AddChild(new PanelContainer
        {
            PanelOverride = CreatePanelStyle(Color.FromHex("#0D151C")),
            VerticalExpand = true,
            HorizontalExpand = true,
            Children =
            {
                new ScrollContainer
                {
                    VerticalExpand = true,
                    HorizontalExpand = true,
                    HScrollEnabled = false,
                    Children = { _results },
                },
            },
        });

        _selectedPanel = new PanelContainer
        {
            PanelOverride = CreatePanelStyle(Color.FromHex("#142431"), Color.FromHex("#4ad9ff")),
            Visible = false,
        };
        var selectedBox = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 2,
        };
        var selectedHeader = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };
        selectedHeader.AddChild(new Label
        {
            Text = Loc.GetString("bank-ui-transfer-selected"),
            StyleClasses = { "LabelSubText" },
            HorizontalExpand = true,
        });
        _changeButton = new Button { Text = Loc.GetString("bank-ui-transfer-change") };
        _changeButton.OnPressed += _ => ChangeRecipient();
        selectedHeader.AddChild(_changeButton);
        selectedBox.AddChild(selectedHeader);
        _selectedName = new Label
        {
            ClipText = true,
            FontColorOverride = Color.FromHex("#F2F4F8"),
        };
        selectedBox.AddChild(_selectedName);
        _selectedAccount = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true,
        };
        selectedBox.AddChild(_selectedAccount);
        _selectedPanel.AddChild(selectedBox);
        _content.AddChild(_selectedPanel);

        _available = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true,
        };
        _content.AddChild(_available);

        _content.AddChild(new Label
        {
            Text = Loc.GetString("bank-ui-transfer-amount"),
            StyleClasses = { "LabelSubText" },
        });
        _amount = new LineEdit { HorizontalExpand = true };
        _amount.OnTextChanged += _ =>
        {
            _confirmPanel!.Visible = false;
            UpdateSendButton();
        };
        _content.AddChild(_amount);

        _sendButton = new Button
        {
            Text = Loc.GetString("bank-ui-transfer-send"),
            HorizontalExpand = true,
            Disabled = true,
        };
        _sendButton.OnPressed += _ => ShowConfirmation();
        _content.AddChild(_sendButton);

        _confirmPanel = new PanelContainer
        {
            PanelOverride = CreatePanelStyle(Color.FromHex("#101820"), Color.FromHex("#D7B56D")),
            Visible = false,
        };
        var confirmBox = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 4,
        };
        _confirmLabel = new Label { FontColorOverride = Color.FromHex("#F2F4F8") };
        confirmBox.AddChild(_confirmLabel);
        var confirmButtons = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        _confirmButton = new Button
        {
            Text = Loc.GetString("bank-ui-transfer-confirm-button"),
            HorizontalExpand = true,
        };
        _confirmButton.OnPressed += _ => ConfirmTransfer();
        _cancelButton = new Button
        {
            Text = Loc.GetString("bank-ui-transfer-cancel"),
            HorizontalExpand = true,
        };
        _cancelButton.OnPressed += _ =>
        {
            _confirmPanel!.Visible = false;
            UpdateSendButton();
        };
        confirmButtons.AddChild(_confirmButton);
        confirmButtons.AddChild(_cancelButton);
        confirmBox.AddChild(confirmButtons);
        _confirmPanel.AddChild(confirmBox);
        _content.AddChild(_confirmPanel);

        _statusLabel = new Label
        {
            ClipText = true,
            Visible = false,
        };
        _content.AddChild(_statusLabel);

        ShowSearchMessage("bank-ui-transfer-search-empty");
        UpdateSendButton();
    }

    public void UpdateState(BankTransferUiState transfer, BankAccountUiState account)
    {
        _hasAccount = !string.IsNullOrWhiteSpace(account.AccountId);
        _balance = Math.Max(0, account.Balance);
        _content.Visible = _hasAccount;
        _noAccountPanel.Visible = !_hasAccount;
        _available.Text = Loc.GetString("bank-ui-transfer-available",
            ("balance", _balance.ToString("N0", CultureInfo.CurrentCulture)));

        if (!_hasAccount)
        {
            ClearResults();
            _selected = null;
            _selectedPanel.Visible = false;
            _confirmPanel!.Visible = false;
            _statusLabel.Visible = false;
            _sendButton.Disabled = true;
            return;
        }

        if (transfer.Query == _query)
            ApplySearchState(transfer);

        if (_awaitingResult && transfer.LastTransferRequestId == _pendingRequestId)
            ApplyTransferResult(transfer);

        UpdateSendButton();
    }

    private void OnSearchTextChanged(string text)
    {
        var query = text.Trim();
        _query = query;
        CancelSearchDebounce();

        if (query.Length == 0)
        {
            ClearResults();
            ShowSearchMessage("bank-ui-transfer-search-empty");
            return;
        }

        ClearResults();
        ShowSearchMessage("bank-ui-transfer-search-loading");
        var cts = new CancellationTokenSource();
        _searchDebounce = cts;
        Timer.Spawn(SearchDebounceMilliseconds, () =>
        {
            if (!Disposed && !cts.IsCancellationRequested)
                OnSearchRequested?.Invoke(query);
        }, cts.Token);
    }

    private void StartSearch(string text)
    {
        var query = text.Trim();
        _query = query;
        CancelSearchDebounce();

        if (query.Length == 0)
        {
            ClearResults();
            ShowSearchMessage("bank-ui-transfer-search-empty");
            return;
        }

        ClearResults();
        ShowSearchMessage("bank-ui-transfer-search-loading");
        OnSearchRequested?.Invoke(query);
    }

    private void CancelSearchDebounce()
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = null;
    }

    private void ApplySearchState(BankTransferUiState transfer)
    {
        switch (transfer.SearchStatus)
        {
            case BankTransferSearchStatus.Results:
                _searchStatus.Visible = false;
                RebuildResults(transfer.Results);
                break;
            case BankTransferSearchStatus.NoResults:
                ClearResults();
                ShowSearchMessage("bank-ui-transfer-search-none");
                break;
            case BankTransferSearchStatus.EmptyQuery:
                ClearResults();
                ShowSearchMessage("bank-ui-transfer-search-empty");
                break;
            case BankTransferSearchStatus.InvalidQuery:
                ClearResults();
                ShowSearchMessage("bank-ui-transfer-search-invalid", error: true);
                break;
            case BankTransferSearchStatus.RateLimited:
                ClearResults();
                ShowSearchMessage("bank-ui-transfer-search-rate-limited", error: true);
                break;
            case BankTransferSearchStatus.NoAccount:
                ShowSearchMessage("bank-ui-transfer-no-account", error: true);
                break;
            case BankTransferSearchStatus.Error:
                ClearResults();
                ShowSearchMessage("bank-ui-transfer-search-error", error: true);
                break;
            case BankTransferSearchStatus.Idle:
                if (_query.Length == 0)
                    ShowSearchMessage("bank-ui-transfer-search-empty");
                break;
        }
    }

    private void RebuildResults(List<BankTransferRecipient> recipients)
    {
        _results.RemoveAllChildren();

        if (recipients.Count == 0)
        {
            ShowSearchMessage("bank-ui-transfer-search-none");
            return;
        }

        foreach (var recipient in recipients)
            _results.AddChild(CreateResultRow(recipient));
    }

    private Control CreateResultRow(BankTransferRecipient recipient)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 2),
            PanelOverride = CreatePanelStyle(Color.FromHex("#101820"), Color.FromHex("#2E3B45")),
        };

        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        var info = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 1,
        };
        info.AddChild(new Label
        {
            Text = recipient.Name,
            ClipText = true,
            FontColorOverride = Color.FromHex("#F2F4F8"),
        });
        if (!string.IsNullOrWhiteSpace(recipient.JobTitle))
        {
            info.AddChild(new Label
            {
                Text = recipient.JobTitle,
                StyleClasses = { "LabelSubText" },
                ClipText = true,
                FontColorOverride = Color.FromHex("#8BA0B0"),
            });
        }
        row.AddChild(info);

        var select = new Button
        {
            Text = Loc.GetString("bank-ui-transfer-select"),
            MinSize = new Vector2(64, 28),
            VerticalAlignment = Control.VAlignment.Center,
        };
        select.OnPressed += _ => SelectRecipient(recipient);
        row.AddChild(select);

        panel.AddChild(row);
        return panel;
    }

    internal void SelectRecipient(BankTransferRecipient recipient)
    {
        if (_selected?.AccountHolder == recipient.AccountHolder)
            return;

        _selected = recipient;
        _selectedName.Text = recipient.Name;
        _selectedAccount.Text = Loc.GetString("bank-ui-masked-account", ("account", recipient.MaskedAccount));
        _selectedPanel.Visible = true;
        _confirmPanel!.Visible = false;
        _statusLabel.Visible = false;
        _amount.SetText(string.Empty, invokeEvent: true);
        UpdateSendButton();
    }

    private void ChangeRecipient()
    {
        _selected = null;
        _selectedPanel.Visible = false;
        _confirmPanel!.Visible = false;
        _statusLabel.Visible = false;
        _amount.SetText(string.Empty, invokeEvent: true);
        _search.GrabKeyboardFocus();
        UpdateSendButton();
    }

    internal void ShowConfirmation()
    {
        if (!_hasAccount || _selected is not { } recipient || !TryGetAmount(out var amount))
            return;

        _confirmLabel.Text = Loc.GetString("bank-ui-transfer-confirm",
            ("amount", amount.ToString("N0", CultureInfo.CurrentCulture)),
            ("name", recipient.Name));
        _confirmPanel.Visible = true;
        _sendButton.Disabled = true;
    }

    internal void ConfirmTransfer()
    {
        if (!_hasAccount || _awaitingResult || _selected is not { } recipient || !TryGetAmount(out var amount))
            return;

        _pendingRequestId = _nextRequestId++;
        _pendingAmount = amount;
        _pendingRecipient = recipient;
        _awaitingResult = true;
        _confirmPanel!.Visible = false;
        ShowStatus(Loc.GetString("bank-ui-transfer-sending"), NeutralColor);
        UpdateSendButton();
        OnTransferRequested?.Invoke(recipient, amount, _pendingRequestId);
    }

    private void ApplyTransferResult(BankTransferUiState transfer)
    {
        _awaitingResult = false;
        var recipientName = _pendingRecipient?.Name ?? string.Empty;
        var amount = transfer.LastTransferAmount != 0 ? transfer.LastTransferAmount : _pendingAmount;

        switch (transfer.LastTransferStatus)
        {
            case BankTransferStatus.Success:
                ShowStatus(Loc.GetString("bank-ui-transfer-success",
                    ("amount", amount.ToString("N0", CultureInfo.CurrentCulture)),
                    ("name", recipientName)), PositiveColor);
                _amount.SetText(string.Empty, invokeEvent: true);
                break;
            case BankTransferStatus.DuplicateRequest:
                ShowStatus(Loc.GetString("bank-ui-transfer-duplicate"), NeutralColor);
                break;
            default:
                ShowStatus(TransferError(transfer.LastTransferStatus), NegativeColor);
                break;
        }

        _pendingRequestId = 0;
        _pendingAmount = 0;
        _pendingRecipient = null;
        UpdateSendButton();
    }

    private bool TryGetAmount(out int amount)
    {
        return int.TryParse(_amount.Text.Trim(), out amount)
            && amount > 0
            && amount <= BankTransactionLimits.MaxTransaction
            && amount <= _balance;
    }

    private void UpdateSendButton()
    {
        _sendButton.Disabled = !_hasAccount
            || _selected == null
            || _awaitingResult
            || !TryGetAmount(out _);
    }

    private void ClearResults()
    {
        _results.RemoveAllChildren();
    }

    private void ShowSearchMessage(string messageKey, bool error = false)
    {
        _searchStatus.Text = Loc.GetString(messageKey);
        _searchStatus.FontColorOverride = Color.FromHex(error ? NegativeColor : NeutralColor);
        _searchStatus.Visible = true;
    }

    private void ShowStatus(string text, string color)
    {
        _statusLabel.Text = text;
        _statusLabel.FontColorOverride = Color.FromHex(color);
        _statusLabel.Visible = true;
    }

    private static string TransferError(BankTransferStatus status)
    {
        return status switch
        {
            BankTransferStatus.InvalidAmount => Loc.GetString("bank-ui-status-invalid-amount"),
            BankTransferStatus.InsufficientFunds => Loc.GetString("bank-ui-status-insufficient-funds"),
            BankTransferStatus.NoAccount => Loc.GetString("bank-ui-status-no-account"),
            BankTransferStatus.RecipientNotFound => Loc.GetString("bank-ui-transfer-error-recipient"),
            BankTransferStatus.RecipientNoAccount => Loc.GetString("bank-ui-transfer-error-recipient-account"),
            BankTransferStatus.RecipientNotOnStation => Loc.GetString("bank-ui-transfer-error-station"),
            BankTransferStatus.SelfTransfer => Loc.GetString("bank-ui-transfer-error-self"),
            BankTransferStatus.DuplicateRequest => Loc.GetString("bank-ui-transfer-duplicate"),
            BankTransferStatus.InvalidRequest => Loc.GetString("bank-ui-transfer-error-invalid"),
            _ => Loc.GetString("bank-ui-transfer-error-failed"),
        };
    }

    private static PanelContainer CreateMessagePanel(string messageKey)
    {
        var panel = new PanelContainer
        {
            PanelOverride = CreatePanelStyle(Color.FromHex("#101820")),
            VerticalAlignment = Control.VAlignment.Top,
        };
        panel.AddChild(new Label
        {
            Text = Loc.GetString(messageKey),
            StyleClasses = { "LabelSubText" },
            HorizontalAlignment = Control.HAlignment.Center,
            Margin = new Thickness(10),
        });
        return panel;
    }

    private static StyleBoxFlat CreatePanelStyle(Color background, Color? border = null)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border ?? background,
            BorderThickness = border == null
                ? new Thickness(0)
                : new Thickness(1),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 8,
            ContentMarginBottomOverride = 8,
        };
    }
}
