using System.Globalization;
using Content.Shared._VanGuard.Economy;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._VanGuard.Economy.UI;

/// <summary>
///     Shared client presentation for the PDA bank cartridge and ATMs. The
///     control only sends an operation request; it never sends a balance.
/// </summary>
public sealed class BankAccountControl : BoxContainer
{
    public event Action<BankTransactionRequest>? OnRequest;

    private readonly Label _accountLabel;
    private readonly Label _balanceLabel;
    private readonly Label _resultLabel;
    private readonly Label _cashLabel;
    private readonly LineEdit _amountInput;
    private readonly Button _depositButton;
    private readonly Button _depositAllButton;
    private readonly Button _withdrawButton;
    private readonly Button _maxButton;
    private readonly List<(Button Button, int Amount)> _quickButtons = new();
    private readonly BoxContainer _history;
    private readonly BoxContainer _operations;
    private readonly Label _stateLabel;
    private int _nextRequestId = Random.Shared.Next(1, int.MaxValue);
    private int _balance;
    private int _cash;
    private bool _loaded;

    public BankAccountControl()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        SeparationOverride = 6;

        var brand = new Label
        {
            Text = Loc.GetString("bank-name"),
            StyleClasses = { "LabelHeading" },
            FontColorOverride = Color.FromHex("#D7B56D"),
        };
        _accountLabel = new Label { StyleClasses = { "LabelSubText" }, ClipText = true };
        _balanceLabel = new Label { StyleClasses = { "LabelHeading" }, FontColorOverride = Color.FromHex("#67E8A5") };
        _cashLabel = new Label
        {
            StyleClasses = { "LabelSubText" },
            FontColorOverride = Color.FromHex("#D7B56D"),
            ClipText = true,
        };
        _stateLabel = new Label
        {
            Text = Loc.GetString("bank-ui-status-loading"),
            StyleClasses = { "LabelSubText" },
            FontColorOverride = Color.FromHex("#9AA4B2"),
        };
        _resultLabel = new Label { FontColorOverride = Color.FromHex("#9AA4B2") };
        _amountInput = new LineEdit
        {
            HorizontalExpand = true,
        };
        _amountInput.OnTextChanged += _ => UpdateButtons();

        var quick = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        foreach (var amount in new[] { 10, 50, 100 })
        {
            var button = new Button { Text = $"+{amount}", HorizontalExpand = true };
            button.OnPressed += _ => _amountInput.SetText(amount.ToString(CultureInfo.InvariantCulture), invokeEvent: true);
            _quickButtons.Add((button, amount));
            quick.AddChild(button);
        }

        _maxButton = new Button { Text = Loc.GetString("bank-ui-max"), HorizontalExpand = true };
        _maxButton.OnPressed += _ =>
            _amountInput.SetText(Math.Min(_balance, BankTransactionLimits.MaxTransaction)
                .ToString(CultureInfo.InvariantCulture), invokeEvent: true);
        quick.AddChild(_maxButton);

        _depositButton = new Button { Text = Loc.GetString("bank-ui-deposit"), HorizontalExpand = true };
        _depositAllButton = new Button { Text = Loc.GetString("bank-ui-deposit-all"), HorizontalExpand = true };
        _withdrawButton = new Button { Text = Loc.GetString("bank-ui-withdraw"), HorizontalExpand = true };
        _depositButton.OnPressed += _ => Send(BankOperation.Deposit);
        _depositAllButton.OnPressed += _ => SendDepositAll();
        _withdrawButton.OnPressed += _ => Send(BankOperation.Withdraw);

        var depositButtons = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        depositButtons.AddChild(_depositButton);
        depositButtons.AddChild(_depositAllButton);

        var withdrawButtons = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        withdrawButtons.AddChild(_withdrawButton);

        _history = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
        _operations = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 5 };
        _operations.AddChild(new Label { Text = Loc.GetString("bank-ui-amount"), StyleClasses = { "LabelSubText" } });
        _operations.AddChild(_amountInput);
        _operations.AddChild(quick);
        _operations.AddChild(depositButtons);
        _operations.AddChild(withdrawButtons);
        _operations.AddChild(_cashLabel);
        _operations.AddChild(_resultLabel);

        AddChild(brand);
        AddChild(_accountLabel);
        AddChild(_balanceLabel);
        AddChild(_stateLabel);
        AddChild(_operations);
        AddChild(new Label { Text = Loc.GetString("bank-ui-history"), StyleClasses = { "LabelSubText" } });
        AddChild(new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
            Children = { _history },
        });
    }

    public void SetOperationsVisible(bool visible)
    {
        _operations.Visible = visible;
    }

    public void UpdateState(BankAccountUiState state)
    {
        _loaded = true;
        _balance = state.Balance;
        _cash = Math.Max(0, state.Cash);
        _accountLabel.Text = state.AccountId == null
            ? Loc.GetString("bank-ui-no-account")
            : Loc.GetString("bank-ui-account", ("account", Mask(state.AccountId)), ("owner", state.OwnerName));
        _balanceLabel.Text = Loc.GetString("bank-ui-balance", ("balance", state.Balance.ToString("N0", CultureInfo.CurrentCulture)));
        _cashLabel.Text = Loc.GetString("bank-ui-cash-available",
            ("cash", _cash.ToString("N0", CultureInfo.CurrentCulture)));
        _resultLabel.Text = ResultText(state);
        _resultLabel.FontColorOverride = state.LastStatus switch
        {
            BankTransactionStatus.Success => Color.FromHex("#67E8A5"),
            BankTransactionStatus.Loading => Color.FromHex("#9AA4B2"),
            BankTransactionStatus.InvalidRequest when state.LastRequestId == 0 => Color.FromHex("#9AA4B2"),
            BankTransactionStatus.DuplicateRequest => Color.FromHex("#D7B56D"),
            _ => Color.FromHex("#E06C75"),
        };
        _stateLabel.Text = string.Empty;
        _stateLabel.Visible = !_operations.Visible && (!_loaded || state.AccountId == null);

        // Every update carries the complete statement. Rebuild the list instead
        // of appending, otherwise repeated BankAccountChangedEvent updates stack
        // duplicate copies of the same transaction in the open view.
        _history.RemoveAllChildren();
        if (state.Transactions.Count == 0)
        {
            _history.AddChild(new Label { Text = Loc.GetString("bank-ui-history-empty") });
        }
        else
        {
            foreach (var transaction in state.Transactions)
            {
                var delta = transaction.Delta >= 0 ? $"+{transaction.Delta:N0}" : $"{transaction.Delta:N0}";
                var time = transaction.Time.ToString(@"hh\:mm");
                var reason = Reason(transaction.Reason);
                if (!string.IsNullOrWhiteSpace(transaction.CounterpartyName))
                    reason = $"{reason}: {transaction.CounterpartyName}";
                _history.AddChild(new Label
                {
                    Text = $"{time}  {delta}  {reason}",
                    ClipText = true,
                    FontColorOverride = transaction.Delta >= 0 ? Color.FromHex("#67E8A5") : Color.FromHex("#E06C75"),
                });
            }
        }

        UpdateButtons();
    }

    private void Send(BankOperation operation)
    {
        if (!TryGetAmount(out var amount))
            return;

        OnRequest?.Invoke(new BankTransactionRequest(_nextRequestId++, operation, amount));
    }

    private void SendDepositAll()
    {
        if (_cash <= 0)
            return;

        // The server recomputes and clamps the actual held cash. The zero here
        // only marks the request as "deposit all"; it is never trusted as a
        // balance or cash amount by the server.
        OnRequest?.Invoke(new BankTransactionRequest(
            _nextRequestId++,
            BankOperation.DepositAll,
            0));
    }

    private bool TryGetAmount(out int amount)
    {
        return int.TryParse(_amountInput.Text.Trim(), out amount)
            && amount > 0
            && amount <= BankTransactionLimits.MaxTransaction;
    }

    private void UpdateButtons()
    {
        var valid = TryGetAmount(out var amount);

        _depositButton.Disabled = !valid || amount > _cash;
        _depositAllButton.Disabled = _cash <= 0;
        _withdrawButton.Disabled = !valid || amount > _balance;
        _maxButton.Disabled = _balance <= 0;

        foreach (var (button, quickAmount) in _quickButtons)
        {
            button.Disabled = quickAmount > BankTransactionLimits.MaxTransaction
                || (quickAmount > _cash && quickAmount > _balance);
        }
    }

    private string ResultText(BankAccountUiState state)
    {
        if (state.LastRequestId == 0 && state.LastStatus == BankTransactionStatus.InvalidRequest)
            return string.Empty;

        if (state.LastStatus != BankTransactionStatus.Success)
            return Loc.GetString(StatusKey(state.LastStatus));

        var amount = state.LastAmount.ToString("N0", CultureInfo.CurrentCulture);
        var balance = state.Balance.ToString("N0", CultureInfo.CurrentCulture);

        return state.LastOperation switch
        {
            BankOperation.DepositAll => Loc.GetString("bank-ui-result-deposit-all",
                ("amount", amount),
                ("balance", balance),
                ("cash", _cash.ToString("N0", CultureInfo.CurrentCulture))),
            BankOperation.Withdraw => Loc.GetString("bank-ui-result-withdraw",
                ("amount", amount),
                ("balance", balance)),
            _ => Loc.GetString("bank-ui-result-deposit",
                ("amount", amount),
                ("balance", balance)),
        };
    }

    private static string Mask(string accountId)
    {
        return accountId.Length <= 4 ? accountId : $"•••• {accountId[^4..]}";
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

    private static string StatusKey(BankTransactionStatus status)
    {
        return status switch
        {
            BankTransactionStatus.Success => "bank-ui-status-success",
            BankTransactionStatus.InvalidAmount => "bank-ui-status-invalid-amount",
            BankTransactionStatus.InsufficientFunds => "bank-ui-status-insufficient-funds",
            BankTransactionStatus.NoAccount => "bank-ui-status-no-account",
            BankTransactionStatus.NoCash => "bank-ui-status-no-cash",
            BankTransactionStatus.CashTooLarge => "bank-ui-status-cash-too-large",
            BankTransactionStatus.Loading => "bank-ui-status-loading",
            BankTransactionStatus.WrongPin => "bank-atm-status-wrong-pin",
            BankTransactionStatus.NotAuthorized => "bank-atm-status-not-authorized",
            BankTransactionStatus.NoCard => "bank-atm-status-no-card",
            BankTransactionStatus.CardMismatch => "bank-atm-status-card-mismatch",
            BankTransactionStatus.DuplicateRequest => "bank-ui-status-duplicate",
            _ => "bank-ui-status-invalid",
        };
    }
}
