using Content.Shared._VanGuard.Economy;
using Robust.Client.UserInterface;

namespace Content.Client._VanGuard.Economy.UI;

public sealed class BankAtmBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private BankAtmWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = new BankAtmWindow();
        _window.OnAuthorize += pin => SendMessage(new BankAtmAuthMessage(pin));
        _window.OnEject += () => SendMessage(new BankAtmEjectMessage());
        _window.OnEndSession += () => SendMessage(new BankAtmEndSessionMessage());
        _window.OnRequest += request => SendMessage(new BankAtmTransactionMessage(request));
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is BankAccountUiState bankState)
            _window?.UpdateState(bankState);
    }

    protected override void Dispose(bool disposing)
    {
        _window?.Close();
        base.Dispose(disposing);
    }
}
