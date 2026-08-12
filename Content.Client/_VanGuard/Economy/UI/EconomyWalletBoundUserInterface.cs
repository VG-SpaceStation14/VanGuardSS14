using Content.Shared._VanGuard.Economy;
using Content.Shared.Roles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._VanGuard.Economy.UI;

public sealed class EconomyWalletBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private EconomyWalletWindow? _window;
    private string? _lastAccountId;
    private int _lastBalance;
    private bool _manualExpanded;
    private bool _accountOverrideEdited;
    private bool _suppressAccountEditTracking;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<EconomyWalletWindow>();

        _window.WithdrawButton.OnPressed += _ => TryWithdraw();
        _window.Quick10Button.OnPressed += _ => SetAmount(10);
        _window.Quick50Button.OnPressed += _ => SetAmount(50);
        _window.Quick100Button.OnPressed += _ => SetAmount(100);
        _window.MaxButton.OnPressed += _ => SetMaxAmount();
        _window.ManualToggleButton.OnPressed += _ => ToggleManualAccount();
        _window.AmountInput.OnTextChanged += _ => UpdateWithdrawAvailability();
        _window.AccountIdInput.OnTextChanged += _ => OnAccountInputChanged();

        _window.MainTabs.SetTabTitle(0, Loc.GetString("economy-card-tab-account"));
        _window.MainTabs.SetTabTitle(1, Loc.GetString("economy-card-tab-operations"));

        UpdateDisplayedAccount();
        UpdateWithdrawAvailability();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (_window == null || state is not EconomyAccountBoundUiState cast)
            return;

        _lastAccountId = cast.AccountId;
        _lastBalance = Math.Max(0, cast.Balance);

        _window.FundsValueLabel.Text = _window.FormatCredits(_lastBalance);
        UpdateDisplayedAccount();
        RebuildHistory(cast.Transactions);

        if (!_manualExpanded && !_accountOverrideEdited && string.IsNullOrWhiteSpace(_window.AccountIdInput.Text))
        {
            _suppressAccountEditTracking = true;
            _window.AccountIdInput.Text = _lastAccountId ?? string.Empty;
            _suppressAccountEditTracking = false;
        }

        UpdateWithdrawAvailability();
    }

    private void OnAccountInputChanged()
    {
        if (_window == null)
            return;

        if (!_suppressAccountEditTracking)
        {
            var input = string.IsNullOrWhiteSpace(_window.AccountIdInput.Text) ? null : _window.AccountIdInput.Text.Trim();
            _accountOverrideEdited = !string.Equals(input, _lastAccountId, StringComparison.Ordinal);
        }

        SendMessage(new EconomySelectAccountMessage(GetEffectiveAccountId()));

        UpdateDisplayedAccount();
        UpdateWithdrawAvailability();
    }

    private void UpdateDisplayedAccount()
    {
        if (_window == null)
            return;

        var masked = _window.MaskAccountId(GetEffectiveAccountId());
        _window.AccountNumberLabel.Text = masked;
        _window.AccountChipLabel.Text = masked;
    }

    private void RebuildHistory(List<EconomyUiTransaction> transactions)
    {
        if (_window == null)
            return;

        _window.HistoryList.RemoveAllChildren();

        if (transactions.Count == 0)
        {
            _window.HistoryList.AddChild(new Label
            {
                Text = Loc.GetString("economy-card-history-empty"),
                FontColorOverride = Color.FromHex("#9AA4B2"),
                Margin = new Thickness(0, 2, 0, 2),
            });
            return;
        }

        foreach (var transaction in transactions)
        {
            var row = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 6,
                Margin = new Thickness(0, 1, 0, 1),
            };

            var deltaText = transaction.Delta >= 0 ? $"+{transaction.Delta:N0}" : $"{transaction.Delta:N0}";
            row.AddChild(new Label
            {
                Text = deltaText,
                FontColorOverride = transaction.Delta >= 0 ? Color.FromHex("#67E8A5") : Color.FromHex("#E06C75"),
                MinWidth = 72,
                HorizontalAlignment = Control.HAlignment.Right,
            });
            row.AddChild(new Label
            {
                Text = LocalizeReason(transaction.Reason, transaction.ReasonData),
                HorizontalExpand = true,
                ClipText = true,
                FontColorOverride = Color.FromHex("#C7CCD1"),
            });

            _window.HistoryList.AddChild(row);
        }
    }

    private string LocalizeReason(string reason, string? reasonData)
    {
        var localized = reason switch
        {
            "payroll" => Loc.GetString("economy-card-history-payroll"),
            "starting-payroll" => Loc.GetString("economy-card-history-starting-payroll"),
            "card-withdraw" => Loc.GetString("economy-card-history-withdraw"),
            "card-deposit" => Loc.GetString("economy-card-history-deposit"),
            "vending-purchase" => Loc.GetString("economy-card-history-vending-purchase"),
            _ => reason,
        };

        if (string.IsNullOrWhiteSpace(reasonData))
            return localized;

        // reasonData for purchases is the prototype id of the bought item;
        // resolve it to its localized display name.
        if (reason == "vending-purchase")
        {
            var protoMan = IoCManager.Resolve<Robust.Shared.Prototypes.IPrototypeManager>();
            if (protoMan.TryIndex(reasonData, out EntityPrototype? proto))
                return $"{localized}: {proto.Name}";
        }

        // reasonData for payroll is the job prototype id; show its localized name.
        if (reason is "payroll" or "starting-payroll")
        {
            var protoMan = IoCManager.Resolve<Robust.Shared.Prototypes.IPrototypeManager>();
            if (protoMan.TryIndex(reasonData, out JobPrototype? job))
                return $"{localized}: {job.LocalizedName}";
        }

        return $"{localized} ({reasonData})";
    }

    private string? GetEffectiveAccountId()
    {
        if (_window == null)
            return _lastAccountId;

        var manualInput = string.IsNullOrWhiteSpace(_window.AccountIdInput.Text) ? null : _window.AccountIdInput.Text.Trim();
        if (_accountOverrideEdited)
            return manualInput;

        return manualInput ?? _lastAccountId;
    }

    private void TryWithdraw()
    {
        if (_window == null)
            return;

        var effectiveAccount = GetEffectiveAccountId();
        if (string.IsNullOrWhiteSpace(effectiveAccount))
            return;

        var amount = GetSafeAmount();
        if (amount <= 0)
            return;

        SendMessage(new EconomyWithdrawMessage(amount, effectiveAccount));
    }

    private int GetSafeAmount()
    {
        if (_window == null)
            return 0;

        var text = _window.AmountInput.Text.Trim();
        if (!int.TryParse(text, out var amount) || amount <= 0)
            return 0;

        return _lastBalance > 0 ? Math.Clamp(amount, 1, _lastBalance) : amount;
    }

    private void SetAmount(int amount)
    {
        if (_window == null)
            return;

        var safe = Math.Max(1, amount);
        if (_lastBalance > 0)
            safe = Math.Clamp(safe, 1, _lastBalance);

        _window.AmountInput.Text = safe.ToString();
        UpdateWithdrawAvailability();
    }

    private void SetMaxAmount()
    {
        if (_window == null)
            return;

        var max = Math.Max(1, _lastBalance);
        _window.AmountInput.Text = max.ToString();
        UpdateWithdrawAvailability();
    }

    private void ToggleManualAccount()
    {
        if (_window == null)
            return;

        _manualExpanded = !_manualExpanded;
        _window.ManualBox.Visible = _manualExpanded;

        if (!_manualExpanded)
            _accountOverrideEdited = false;
    }

    private void UpdateWithdrawAvailability()
    {
        if (_window == null)
            return;

        var hasAccount = !string.IsNullOrWhiteSpace(GetEffectiveAccountId());
        var hasFunds = _lastBalance > 0;
        var amount = GetSafeAmount();
        var canWithdraw = hasAccount && hasFunds && amount > 0;

        _window.WithdrawButton.Disabled = !canWithdraw;
        _window.MaxButton.Disabled = !hasFunds;

        if (!hasAccount)
        {
            _window.WithdrawHintLabel.Text = Loc.GetString("economy-card-status-no-account");
            _window.WithdrawHintLabel.FontColorOverride = Color.FromHex("#E06C75");
            _window.WithdrawButton.ModulateSelfOverride = Color.FromHex("#C85D66");
            return;
        }

        if (!hasFunds)
        {
            _window.WithdrawHintLabel.Text = Loc.GetString("economy-card-status-no-funds");
            _window.WithdrawHintLabel.FontColorOverride = Color.FromHex("#E06C75");
            _window.WithdrawButton.ModulateSelfOverride = Color.FromHex("#C85D66");
            return;
        }

        _window.WithdrawHintLabel.Text = Loc.GetString("economy-card-withdraw-hint");
        _window.WithdrawHintLabel.FontColorOverride = Color.FromHex("#9AA4B2");
        _window.WithdrawButton.ModulateSelfOverride = Color.FromHex("#53C78F");
    }
}
