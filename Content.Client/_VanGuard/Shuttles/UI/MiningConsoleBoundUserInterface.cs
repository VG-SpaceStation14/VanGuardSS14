using Content.Shared._VanGuard.Shuttles;
using Robust.Shared.GameObjects;

namespace Content.Client._VanGuard.Shuttles.UI;

public sealed class MiningConsoleBoundUserInterface : BoundUserInterface
{
    private MiningConsoleWindow? _window;

    public MiningConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new MiningConsoleWindow(Owner)
        {
            Title = Loc.GetString("mining-console-title"),
        };
        _window.OnTravel += index =>
            SendMessage(new MiningShuttleConsoleTravelMessage(index));
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is MiningShuttleConsoleState cast)
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
