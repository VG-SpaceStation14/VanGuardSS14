using Content.Shared._VanGuard.Shuttles;
using Robust.Shared.GameObjects;

namespace Content.Client._VanGuard.Shuttles.UI;

public sealed class GuestShutterConsoleBoundUserInterface : BoundUserInterface
{
    private GuestShutterConsoleWindow? _window;

    public GuestShutterConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new GuestShutterConsoleWindow(Owner)
        {
            Title = Loc.GetString("guest-shutter-title"),
        };
        _window.OnToggle += door => SendMessage(new GuestShutterToggleMessage(door));
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is GuestShutterConsoleState cast)
            _window?.UpdateState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _window?.Dispose();
            _window = null;
        }

        base.Dispose(disposing);
    }
}
