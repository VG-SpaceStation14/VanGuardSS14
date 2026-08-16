using Content.Client._VanGuard.Mining.OreBags.UI;
using Content.Shared._VanGuard.Mining.OreBags;
using JetBrains.Annotations;
using Robust.Shared.Network;

namespace Content.Client._VanGuard.Mining.OreBags;

/// <summary>
/// Client-side controller for smart ore bags: opens the filter window when the
/// server requests it and sends the new ignore list back on confirm.
/// </summary>
[UsedImplicitly]
public sealed class SmartOreBagUIController : EntitySystem
{
    private SmartOreBagWindow? _currentWindow;
    private NetEntity _currentEntity;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<OpenSmartOreBagWindowMessage>(OnOpenWindow);
    }

    private void OnOpenWindow(OpenSmartOreBagWindowMessage msg)
    {
        _currentEntity = msg.Entity;

        _currentWindow = new SmartOreBagWindow();
        _currentWindow.UpdateState(msg.IgnoredOres);

        _currentWindow.OnConfirmed += ignoredOres =>
        {
            var updateMsg = new SmartOreBagUpdateMessage(_currentEntity, ignoredOres);
            RaiseNetworkEvent(updateMsg);

            _currentWindow = null;
        };

        _currentWindow.OpenCentered();
    }
}
