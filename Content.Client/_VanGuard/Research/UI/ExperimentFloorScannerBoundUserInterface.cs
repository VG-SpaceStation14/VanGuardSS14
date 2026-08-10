using Content.Shared._VanGuard.Research.Components;
using Content.Shared.Research.Components;
using Robust.Client.UserInterface;

namespace Content.Client._VanGuard.Research.UI;

/// <summary>
/// Bound user interface for the floor experiment scanner.
/// </summary>
public sealed class ExperimentFloorScannerBoundUserInterface : BoundUserInterface
{
    private ExperimentFloorScannerMenu? _menu;

    public ExperimentFloorScannerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<ExperimentFloorScannerMenu>();
        _menu.OpenCentered();
        _menu.OnClose += Close;
        _menu.OnSelectOrder += id => SendMessage(new ExperimentSelectOrderMessage(id));
        _menu.OnAbandonOrder += () => SendMessage(new ExperimentAbandonOrderMessage());
        _menu.OnSkipOrder += id => SendMessage(new ExperimentSkipOrderMessage(id));
        _menu.OnSelectServer += () => SendMessage(new ConsoleServerSelectionMessage());
        _menu.OnPerform += () => SendMessage(new ExperimentFloorScannerPerformMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not ExperimentFloorScannerState scannerState)
            return;
        _menu?.UpdateState(scannerState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        if (_menu == null) return;
        _menu.OnClose -= Close;
        _menu.Dispose();
        _menu = null;
    }
}
