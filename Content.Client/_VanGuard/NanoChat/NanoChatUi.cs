using Content.Client.UserInterface.Fragments;
using Content.Shared._VanGuard.NanoChat;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._VanGuard.NanoChat;

/// <summary>
///     UI fragment for the NanoChat cartridge. Bridges the PDA cartridge UI
///     with <see cref="NanoChatUiFragment"/> and forwards user actions to the
///     server as <see cref="NanoChatUiMessageEvent"/>s.
/// </summary>
public sealed partial class NanoChatUi : UIFragment
{
    private NanoChatUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment?.Dispose();
        _fragment = new NanoChatUiFragment();
        _fragment.OnMessageSent += (type, number, content, job) =>
        {
            var message = new NanoChatUiMessageEvent(type, number, content, job);
            userInterface.SendMessage(new CartridgeUiMessage(message));
        };
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is NanoChatUiState cast && _fragment is { Disposed: false })
            _fragment?.UpdateState(cast);
    }
}
