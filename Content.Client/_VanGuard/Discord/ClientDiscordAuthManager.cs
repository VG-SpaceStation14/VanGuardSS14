using System;
using Content.Shared._VanGuard.Discord;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Client._VanGuard.Discord;

public sealed partial class ClientDiscordAuthManager
{
    [Dependency] private IClientNetManager _net = default!;

    public event Action<string>? OnCodeReceived;
    public event Action<bool, int, string, string>? OnStateReceived;
    public int CurrentTier { get; private set; }

    public void Initialize()
    {
        _net.RegisterNetMessage<RequestDiscordStateMessage>();
        _net.RegisterNetMessage<RequestDiscordCodeMessage>();
        _net.RegisterNetMessage<DiscordLinkCodeMessage>(OnCode);
        _net.RegisterNetMessage<DiscordLinkStateMessage>(OnState);
    }

    public void RequestState() => _net.ClientSendMessage(new RequestDiscordStateMessage());
    public void RequestCode() => _net.ClientSendMessage(new RequestDiscordCodeMessage());

    private void OnCode(DiscordLinkCodeMessage msg) => OnCodeReceived?.Invoke(msg.Code);
    private void OnState(DiscordLinkStateMessage msg) 
    {
        CurrentTier = msg.Tier;
        OnStateReceived?.Invoke(msg.IsLinked, msg.Tier, msg.TierName, msg.TimeLeft);
    }
}
