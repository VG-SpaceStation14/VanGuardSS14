using Content.Shared._VanGuard.Shuttles;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._VanGuard.Shuttles.UI;

/// <summary>
/// Minimal destination-list UI for the mining shuttle console. It intentionally
/// has no free-flight controls: each button asks the server to dock at a tagged
/// destination.
/// </summary>
public sealed class MiningShuttleConsoleBoundUserInterface : BoundUserInterface
{
    private DefaultWindow? _window;
    private BoxContainer? _dockList;
    private Label? _status;

    public MiningShuttleConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new DefaultWindow
        {
            Title = Loc.GetString("mining-shuttle-console-title"),
            MinSize = new Vector2(320, 260),
        };
        _window.OnClose += Close;

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
        };
        _window.Contents.AddChild(root);

        _status = new Label
        {
            Text = Loc.GetString("mining-shuttle-console-select-destination"),
            HorizontalExpand = true,
        };
        root.AddChild(_status);

        var scroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
            HScrollEnabled = false,
        };
        root.AddChild(scroll);

        _dockList = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };
        scroll.AddChild(_dockList);

        var hint = new Label
        {
            Text = Loc.GetString("mining-shuttle-console-hint"),
            StyleClasses = { "LabelSubText" },
        };
        root.AddChild(hint);

        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is MiningShuttleConsoleState consoleState)
            ApplyState(consoleState);
    }

    private void ApplyState(MiningShuttleConsoleState state)
    {
        if (_window == null || _dockList == null || _status == null)
            return;

        _status.Text = state.Error ?? Loc.GetString("mining-shuttle-console-select-destination");
        _dockList.RemoveAllChildren();

        foreach (var entry in state.Docks)
        {
            var button = new Button
            {
                Text = entry.Name,
                Disabled = !entry.Enabled,
                HorizontalExpand = true,
                ToolTip = entry.Reason,
            };

            if (entry.Enabled)
            {
                var dock = entry.Dock;
                button.OnPressed += _ =>
                {
                    SendMessage(new MiningShuttleConsoleTravelMessage(dock));
                };
            }

            _dockList.AddChild(button);
        }

        if (state.Docks.Count == 0 && state.Error == null)
        {
            _dockList.AddChild(new Label
            {
                Text = Loc.GetString("mining-shuttle-console-no-destinations"),
                StyleClasses = { "LabelSubText" },
            });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dockList?.RemoveAllChildren();
            _window?.Dispose();
            _window = null;
        }

        base.Dispose(disposing);
    }
}
