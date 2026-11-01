using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Shared._VanGuard.CCVars;
using Robust.Shared.Configuration;
using Robust.Shared.Log;

namespace Content.Server._VanGuard.Discord;

/// <summary>
/// Posts a compact ban summary to the configured Discord webhook. The transport is deliberately
/// fire-and-forget: a failed webhook must never interrupt the ban command itself.
/// </summary>
public sealed partial class VgBanWebhookSender : IPostInjectInit
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Dependency] private IConfigurationManager _cfg = default!;

    private readonly HttpClient _http = new();
    private ISawmill _log = default!;
    private string _webhook = string.Empty;
    private bool _enabled;

    public void PostInject()
    {
        _log = Logger.GetSawmill("vg_ban_webhook");

        // Tests can construct the IoC graph before server-only CVars are registered.
        if (_cfg.IsCVarRegistered(VGCCVars.DiscordBanWebhook.Name))
        {
            _cfg.OnValueChanged(VGCCVars.DiscordBanWebhook, url => _webhook = url, true);
            _webhook = _cfg.GetCVar(VGCCVars.DiscordBanWebhook);
        }

        if (_cfg.IsCVarRegistered(VGCCVars.DiscordBanWebhookEnabled.Name))
        {
            _cfg.OnValueChanged(VGCCVars.DiscordBanWebhookEnabled, enabled => _enabled = enabled, true);
            _enabled = _cfg.GetCVar(VGCCVars.DiscordBanWebhookEnabled);
        }

        _log.Info($"Ban webhook ready (enabled={_enabled}, configured={!string.IsNullOrEmpty(_webhook)})");
    }

    public async Task SendBanAsync(VgBanInfo info)
    {
        if (!_enabled)
        {
            _log.Debug("Ban webhook is disabled, skipping message");
            return;
        }

        if (string.IsNullOrWhiteSpace(_webhook))
        {
            _log.Warning("Ban webhook is enabled but no URL is configured");
            return;
        }

        try
        {
            var payload = BuildPayload(info);
            using var content = JsonContent.Create(payload, options: JsonOptions);
            using var response = await _http.PostAsync(_webhook, content);

            if (response.IsSuccessStatusCode)
            {
                _log.Info($"Ban {info.BanId} delivered to Discord");
                return;
            }

            var body = await response.Content.ReadAsStringAsync();
            _log.Error($"Discord rejected ban {info.BanId}: {(int) response.StatusCode} {body}");
        }
        catch (Exception ex)
        {
            _log.Error($"Could not deliver ban {info.BanId} to Discord: {ex.Message}");
        }
    }

    private static WebhookPayload BuildPayload(VgBanInfo info)
    {
        return new WebhookPayload
        {
            Embeds =
            {
                new WebhookEmbed
                {
                    Title = BuildTitle(info),
                    Color = ColorFor(info.BanType),
                    Description = BuildDescription(info),
                    Footer = new WebhookEmbedFooter
                    {
                        Text = $"VG • {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC",
                    },
                    Timestamp = DateTimeOffset.UtcNow,
                },
            },
        };
    }

    private static uint ColorFor(string banType)
    {
        return banType switch
        {
            "Role" => 0xFFA500u,
            "Department" => 0xFFEA00u,
            "Panel" => 0x9828C9u,
            _ => 0xFF0000u,
        };
    }

    private static string BuildTitle(VgBanInfo info)
    {
        var label = info.BanType switch
        {
            "Role" => "⚔️ Ролевой бан",
            "Department" => "🏢 Департаментный бан",
            "Panel" => "📋 Панельный бан",
            _ => "🔨 Серверный бан",
        };

        return $"{label} #{info.BanId}";
    }

    private static string BuildDescription(VgBanInfo info)
    {
        var lines = new StringBuilder();
        lines.AppendLine($"**Игрок:** {info.Target}");
        lines.AppendLine($"**Причина:** {info.Reason}");
        lines.AppendLine($"**Админ:** {info.AdminName}");

        if (info.Minutes == 0)
        {
            lines.AppendLine("**Длительность:** Перманентный");
        }
        else
        {
            var duration = TimeSpan.FromMinutes(info.Minutes);
            lines.AppendLine($"**Длительность:** {duration.Days}д {duration.Hours}ч {duration.Minutes}м");

            if (info.Expires is { } expires)
                lines.AppendLine($"**Истекает:** {expires:yyyy-MM-dd HH:mm} UTC");
        }

        if (info.AdditionalInfo.TryGetValue("role", out var role))
            lines.AppendLine($"**Роль:** {role}");
        if (info.AdditionalInfo.TryGetValue("department", out var department))
            lines.AppendLine($"**Департамент:** {department}");
        if (info.AdditionalInfo.TryGetValue("panelData", out var panel))
            lines.AppendLine($"**Забанено:** {panel}");

        return lines.ToString();
    }
}
