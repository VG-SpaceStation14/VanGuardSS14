using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Content.Shared._VanGuard.Discord;
using Content.Shared._VanGuard.CCVars;
using Npgsql;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._VanGuard.Discord;

public sealed partial class VgDiscordAuthManager
{
    [Dependency] private DiscordDb _db = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IServerNetManager _net = default!;
    [Dependency] private IPlayerManager _player = default!;

    private HttpClient _http = new();
    private ISawmill _log = default!;

    public void Initialize()
    {
        _log = Logger.GetSawmill("vg_discord_auth");

        _net.RegisterNetMessage<RequestDiscordLinkMessage>(OnRequestLink);
        _net.RegisterNetMessage<DiscordLinkCodeMessage>();
        _net.RegisterNetMessage<DiscordLinkStateMessage>();


        _player.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        // Нас интересует момент, когда игрок подключился к серверу
        if (e.NewStatus == SessionStatus.Connected)
        {
            _ = SendCurrentState(e.Session);
        }
    }

    private async void OnRequestLink(RequestDiscordLinkMessage msg)
    {
        var session = _player.GetSessionByChannel(msg.MsgChannel);
        if (session == null || _db.DataSource == null) return;

        await SendCurrentState(session);

        await using var conn = await _db.DataSource.OpenConnectionAsync();
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO discord_link_requests (user_id, status, requested_at)
            VALUES (@uid, 'requested', now())
            ON CONFLICT (user_id) DO UPDATE
            SET status = 'requested', link_code = NULL, expires_at = NULL, requested_at = now()
            WHERE discord_link_requests.status <> 'linked'
            """, conn))
        {
            cmd.Parameters.AddWithValue("uid", session.UserId.UserId);
            await cmd.ExecuteNonQueryAsync();
        }

        _ = PingBotAsync(session.UserId.UserId);

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(2000);

                await using var pollConn = await _db.DataSource.OpenConnectionAsync();
                await using var pollCmd = new NpgsqlCommand("""
                    SELECT link_code FROM discord_link_requests 
                    WHERE user_id = @uid AND status = 'ready' AND expires_at > now()
                    """, pollConn);
                pollCmd.Parameters.AddWithValue("uid", session.UserId.UserId);

                if (await pollCmd.ExecuteScalarAsync() is string code)
                {
                    _net.ServerSendMessage(new DiscordLinkCodeMessage { Code = code }, session.Channel);
                    break;
                }
            }
        });
    }

    public async Task SendCurrentState(ICommonSession session)
    {
        if (_db.DataSource == null) return;

        await using var conn = await _db.DataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT status, sponsor_tier, sponsor_until 
            FROM discord_link_requests 
            WHERE user_id = @uid
            """, conn);
        cmd.Parameters.AddWithValue("uid", session.UserId.UserId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync() && reader.GetString(0) == "linked")
        {
            var tier = reader.GetInt16(1);
            var until = reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2);
            var isActive = until > DateTime.UtcNow;

            var tierName = tier switch {
                3 => "Космический ниндзя",
                2 => "Революционер",
                1 => "Агент Синдиката",
                _ => "Нет"
            };

            var timeLeft = isActive ? $"Активна до {until:dd.MM.yyyy HH:mm} UTC" : "Истекла";

            _net.ServerSendMessage(new DiscordLinkStateMessage
            {
                IsLinked = true,
                Tier = isActive ? tier : 0,
                TierName = isActive ? tierName : "Нет",
                TimeLeft = timeLeft
            }, session.Channel);
            return;
        }

        _net.ServerSendMessage(new DiscordLinkStateMessage { IsLinked = false }, session.Channel);
    }

    private async Task PingBotAsync(Guid userId)
    {
        try
        {
            var secret = _cfg.GetCVar(VGCCVars.DiscordBotSecret);
            var payload = new { user_id = userId.ToString(), secret = secret };
            using var content = JsonContent.Create(payload);
            using var response = await _http.PostAsync("http://127.0.0.1:8080/api/generate_code", content);
        }
        catch (Exception ex)
        {
            _log.Warning($"Bot ping failed: {ex.Message}. Falling back to DB polling.");
        }
    }
}
