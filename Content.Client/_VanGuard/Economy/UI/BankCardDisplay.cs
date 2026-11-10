using Content.Shared._VanGuard.Economy;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._VanGuard.Economy.UI;

/// <summary>
///     Visual account card shared by the ATM "Accounts" tab and the PDA app.
/// </summary>
public sealed class BankCardDisplay : PanelContainer
{
    private readonly Label _bankName;
    private readonly Label _owner;
    private readonly Label _number;
    private readonly Label _balance;

    public BankCardDisplay()
    {
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#142431"),
            BorderColor = Color.FromHex("#4ad9ff"),
            BorderThickness = new Thickness(0, 0, 0, 0),
            ContentMarginLeftOverride = 18,
            ContentMarginRightOverride = 18,
            ContentMarginTopOverride = 16,
            ContentMarginBottomOverride = 16,
        };
        MinHeight = 150;

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5,
        };
        AddChild(root);

        _bankName = new Label
        {
            Text = Loc.GetString("bank-name"),
            StyleClasses = { "LabelHeading" },
            FontColorOverride = Color.FromHex("#D7B56D"),
        };
        root.AddChild(_bankName);

        _owner = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true,
            FontColorOverride = Color.FromHex("#F2F4F8"),
        };
        root.AddChild(_owner);

        _number = new Label
        {
            ClipText = true,
            FontColorOverride = Color.FromHex("#9AA4B2"),
        };
        root.AddChild(_number);

        var spacer = new Control { VerticalExpand = true };
        root.AddChild(spacer);

        _balance = new Label
        {
            StyleClasses = { "LabelHeading" },
            FontColorOverride = Color.FromHex("#67E8A5"),
        };
        root.AddChild(_balance);
    }

    public void UpdateState(BankAccountUiState state)
    {
        _owner.Text = state.AccountId == null
            ? Loc.GetString("bank-ui-no-account")
            : state.OwnerName;
        _number.Text = state.AccountId == null
            ? string.Empty
            : Loc.GetString("bank-ui-masked-account", ("account", Mask(state.AccountId)));
        _balance.Text = Loc.GetString("bank-ui-balance", ("balance", state.Balance.ToString("N0")));
    }

    private static string Mask(string accountId)
    {
        return accountId.Length <= 4 ? accountId : $"•••• •••• •••• {accountId[^4..]}";
    }
}
