using System.Globalization;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Fragments;
using Content.Shared._VanGuard.Economy;
using Content.Shared.CartridgeLoader;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._VanGuard.Economy.UI;

public sealed partial class BankCartridgeUi : UIFragment
{
    private const string PositiveColor = "#67E8A5";
    private const string NegativeColor = "#E06C75";

    private BoxContainer? _root;
    private BoxContainer? _content;
    private BoxContainer? _history;
    private Label? _balanceValue;
    private Label? _ownerValue;
    private Label? _accountValue;
    private Label? _historyCount;
    private Label? _loadingLabel;
    private PanelContainer? _noAccountPanel;
    private BankTransferControl? _transferControl;
    private BoundUserInterface? _ui;

    public override Control GetUIFragmentRoot()
    {
        return _root!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _ui = userInterface;
        _root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Margin = new Thickness(8),
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 1,
        };
        header.AddChild(new Label
        {
            Text = Loc.GetString("bank-name"),
            StyleClasses = { "LabelHeading" },
            FontColorOverride = Color.FromHex("#D7B56D"),
        });
        header.AddChild(new Label
        {
            Text = Loc.GetString("bank-ui-personal-account"),
            StyleClasses = { "LabelSubText" },
            FontColorOverride = Color.FromHex("#9AA4B2"),
        });
        _root.AddChild(header);

        _loadingLabel = new Label
        {
            Text = Loc.GetString("bank-ui-status-loading"),
            StyleClasses = { "LabelSubText" },
            HorizontalAlignment = Control.HAlignment.Center,
            Margin = new Thickness(0, 12),
        };

        _noAccountPanel = CreateMessagePanel("bank-ui-no-account");
        _noAccountPanel.Visible = false;

        _content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false,
        };

        var balanceBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
        };
        balanceBox.AddChild(new Label
        {
            Text = Loc.GetString("bank-ui-available-balance"),
            StyleClasses = { "LabelSubText" },
            FontColorOverride = Color.FromHex("#9AA4B2"),
        });
        _balanceValue = new Label
        {
            StyleClasses = { "LabelHeadingBigger" },
            FontColorOverride = Color.FromHex(PositiveColor),
            ClipText = true,
        };
        balanceBox.AddChild(_balanceValue);
        _content.AddChild(new PanelContainer
        {
            PanelOverride = CreatePanelStyle(Color.FromHex("#142431"), Color.FromHex("#4ad9ff")),
            Children = { balanceBox },
        });

        var detailsBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
        };
        detailsBox.AddChild(new Label
        {
            Text = Loc.GetString("bank-ui-account-holder"),
            StyleClasses = { "LabelSubText" },
            FontColorOverride = Color.FromHex("#9AA4B2"),
        });
        _ownerValue = new Label
        {
            ClipText = true,
            FontColorOverride = Color.FromHex("#F2F4F8"),
        };
        detailsBox.AddChild(_ownerValue);
        _accountValue = new Label
        {
            ClipText = true,
            FontColorOverride = Color.FromHex("#9AA4B2"),
        };
        detailsBox.AddChild(_accountValue);
        _content.AddChild(new PanelContainer
        {
            PanelOverride = CreatePanelStyle(Color.FromHex("#101820")),
            Children = { detailsBox },
        });

        _content.AddChild(new Separator { StyleClasses = { "LowDivider" } });

        var historyHeader = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        historyHeader.AddChild(new Label
        {
            Text = Loc.GetString("bank-ui-history"),
            StyleClasses = { "LabelHeading" },
            HorizontalExpand = true,
            ClipText = true,
        });
        _historyCount = new Label
        {
            StyleClasses = { "LabelSubText" },
            HorizontalAlignment = Control.HAlignment.Right,
            VerticalAlignment = Control.VAlignment.Center,
            ClipText = true,
        };
        historyHeader.AddChild(_historyCount);
        _content.AddChild(historyHeader);

        var history = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };
        _history = history;
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
                    Children = { history },
                },
            },
        });

        _transferControl = new BankTransferControl();
        _transferControl.OnSearchRequested += query =>
            _ui?.SendMessage(new CartridgeUiMessage(new BankCartridgeSearchMessage(query)));
        _transferControl.OnTransferRequested += (recipient, amount, requestId) =>
            _ui?.SendMessage(new CartridgeUiMessage(
                new BankCartridgeTransferMessage(recipient.AccountHolder, amount, requestId)));

        var overview = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        overview.AddChild(_loadingLabel!);
        overview.AddChild(_noAccountPanel!);
        overview.AddChild(_content!);

        var tabs = new TabContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        tabs.AddChild(overview);
        tabs.AddChild(_transferControl);
        tabs.SetTabTitle(0, Loc.GetString("bank-tab-overview"));
        tabs.SetTabTitle(1, Loc.GetString("bank-tab-transfers"));
        _root!.AddChild(tabs);
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not BankCartridgeUiState cartridgeState ||
            _content == null ||
            _balanceValue == null ||
            _ownerValue == null ||
            _accountValue == null ||
            _historyCount == null ||
            _history == null ||
            _loadingLabel == null ||
            _noAccountPanel == null ||
            _transferControl == null)
        {
            return;
        }

        var bankState = cartridgeState.Account;
        _transferControl.UpdateState(cartridgeState.Transfer, bankState);
        _loadingLabel.Visible = false;

        var hasAccount = !string.IsNullOrWhiteSpace(bankState.AccountId);
        _content.Visible = hasAccount;
        _noAccountPanel.Visible = !hasAccount;

        _history.RemoveAllChildren();

        if (!hasAccount)
            return;

        _balanceValue.Text = Loc.GetString("bank-ui-balance-amount",
            ("balance", bankState.Balance.ToString("N0", CultureInfo.CurrentCulture)));
        _ownerValue.Text = string.IsNullOrWhiteSpace(bankState.OwnerName)
            ? "—"
            : bankState.OwnerName;
        _accountValue.Text = Loc.GetString("bank-ui-masked-account",
            ("account", Mask(bankState.AccountId!)));
        _historyCount.Text = Loc.GetString("bank-ui-history-count",
            ("count", bankState.Transactions.Count.ToString(CultureInfo.CurrentCulture)));
        _historyCount.Visible = bankState.Transactions.Count > 0;

        if (bankState.Transactions.Count == 0)
        {
            _history.AddChild(new Label
            {
                Text = Loc.GetString("bank-ui-history-empty"),
                StyleClasses = { "LabelSubText" },
                HorizontalAlignment = Control.HAlignment.Center,
                Margin = new Thickness(0, 12),
            });
            return;
        }

        foreach (var transaction in bankState.Transactions)
        {
            _history.AddChild(CreateHistoryRow(transaction));
        }
    }

    private static PanelContainer CreateHistoryRow(BankUiTransaction transaction)
    {
        var positive = transaction.Delta >= 0;
        var accent = Color.FromHex(positive ? PositiveColor : NegativeColor);

        var row = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#101820"),
                BorderColor = accent,
                BorderThickness = new Thickness(2, 0, 0, 0),
                ContentMarginLeftOverride = 10,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6,
            },
            Margin = new Thickness(0, 0, 0, 2),
        };

        var rowBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true,
        };

        var details = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 1,
            HorizontalExpand = true,
        };
        details.AddChild(new Label
        {
            Text = Reason(transaction.Reason),
            ClipText = true,
            FontColorOverride = Color.FromHex("#F2F4F8"),
        });
        details.AddChild(new Label
        {
            Text = FormatTime(transaction.Time),
            StyleClasses = { "LabelSubText" },
            FontColorOverride = Color.FromHex("#9AA4B2"),
        });

        if (transaction.Reason is "bank-transfer-out" or "bank-transfer-in"
            && !string.IsNullOrWhiteSpace(transaction.CounterpartyName))
        {
            var counterpartyKey = transaction.Reason == "bank-transfer-out"
                ? "bank-ui-history-transfer-to"
                : "bank-ui-history-transfer-from";
            details.AddChild(new Label
            {
                Text = Loc.GetString(counterpartyKey,
                    ("name", transaction.CounterpartyName),
                    ("account", transaction.CounterpartyAccount ?? string.Empty)),
                StyleClasses = { "LabelSubText" },
                ClipText = true,
                FontColorOverride = Color.FromHex("#8BA0B0"),
            });
        }
        rowBox.AddChild(details);

        var delta = transaction.Delta >= 0
            ? $"+{transaction.Delta:N0}"
            : $"{transaction.Delta:N0}";
        rowBox.AddChild(new Label
        {
            Text = delta,
            FontColorOverride = accent,
            HorizontalAlignment = Control.HAlignment.Right,
            VerticalAlignment = Control.VAlignment.Center,
            ClipText = true,
        });

        row.AddChild(rowBox);
        return row;
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
            ContentMarginLeftOverride = 12,
            ContentMarginRightOverride = 12,
            ContentMarginTopOverride = 10,
            ContentMarginBottomOverride = 10,
        };
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.TotalDays >= 1
            ? time.ToString(@"d\.hh\:mm")
            : time.ToString(@"hh\:mm");
    }

    private static string Mask(string accountId)
    {
        return BankAccountFormat.Mask(accountId);
    }

    private static string Reason(string reason)
    {
        return reason switch
        {
            "bank-deposit" => Loc.GetString("bank-ui-history-deposit"),
            "bank-withdraw" => Loc.GetString("bank-ui-history-withdraw"),
            "starting-payroll" => Loc.GetString("bank-ui-history-starting-payroll"),
            "payroll" => Loc.GetString("bank-ui-history-payroll"),
            "vending-purchase" => Loc.GetString("bank-ui-history-purchase"),
            "bank-transfer-out" => Loc.GetString("bank-ui-history-transfer-out"),
            "bank-transfer-in" => Loc.GetString("bank-ui-history-transfer-in"),
            _ => reason,
        };
    }
}
