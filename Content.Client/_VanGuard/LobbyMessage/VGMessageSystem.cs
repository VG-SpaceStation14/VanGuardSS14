using Content.Shared._VanGuard.LobbyMessage;
using Robust.Client.State;
using Robust.Shared.Network;

namespace Content.Client._VanGuard.LobbyMessage;

public sealed partial class VGMessageSystem : EntitySystem
{
    [Dependency] private IStateManager _state = default!;
    [Dependency] private IClientNetManager _net = default!;

    private VGMessagePanel? _panel;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<VGMessageEvent>(OnMessage);
        _state.OnStateChanged += OnStateChanged;
    }

    private void OnMessage(VGMessageEvent msg)
    {
        _panel?.SetMessage(msg.Text);
    }

    private void OnStateChanged(StateChangedEventArgs args)
    {
        if (args.NewState is Content.Client.Lobby.LobbyState lobby && lobby.Lobby is { } gui)
        {
            if (_panel == null)
            {
                _panel = new VGMessagePanel();
                gui.AddLobbyMessagePanel(_panel);
            }

            // Запрашиваем текущее сообщение лобби у сервера
            var request = _net.CreateNetMessage<MsgVGMessageRequest>();
            _net.ClientSendMessage(request);
        }
        else
        {
            _panel?.SetMessage(null);
            _panel = null;
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _state.OnStateChanged -= OnStateChanged;
    }
}