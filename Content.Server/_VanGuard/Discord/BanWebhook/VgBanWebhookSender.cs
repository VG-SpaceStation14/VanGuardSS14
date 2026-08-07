using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Shared._VanGuard.CCVars;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Content.Server._VanGuard.Discord;

public sealed partial class VgBanWebhookSender : IPostInjectInit
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private readonly HttpClient _httpClient = new();
    private ISawmill _sawmill = default!;
    private string _webhookUrl = string.Empty;
    private bool _isEnabled = false;

    public void PostInject()
    {
        _sawmill = Logger.GetSawmill("vg_ban_webhook");

        // These CVars are SERVERONLY and may not be registered yet when PostInject runs
        // (unit tests build the IoC graph before loading CVars), so guard the subscriptions.
        if (_cfg.IsCVarRegistered(VGCCVars.DiscordBanWebhook.Name))
        {
            _cfg.OnValueChanged(VGCCVars.DiscordBanWebhook,
                value => _webhookUrl = value, true);
            _webhookUrl = _cfg.GetCVar(VGCCVars.DiscordBanWebhook);
        }

        if (_cfg.IsCVarRegistered(VGCCVars.DiscordBanWebhookEnabled.Name))
        {
            _cfg.OnValueChanged(VGCCVars.DiscordBanWebhookEnabled,
                value => _isEnabled = value, true);
            _isEnabled = _cfg.GetCVar(VGCCVars.DiscordBanWebhookEnabled);
        }

        _sawmill.Info($"VgBanWebhookSender initialized. Enabled: {_isEnabled}, Webhook: {(string.IsNullOrEmpty(_webhookUrl) ? "not set" : "set")}");
    }

    public async Task SendBanAsync(VgBanInfo info)
    {
        if (!_isEnabled || string.IsNullOrEmpty(_webhookUrl))
        {
            if (!_isEnabled)
                _sawmill.Debug("VG ban webhook is disabled");
            if (string.IsNullOrEmpty(_webhookUrl))
                _sawmill.Warning("VG ban webhook URL is not configured");
            return;
        }

        try
        {
            var payload = GeneratePayload(info);
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_webhookUrl, content);

            if (response.IsSuccessStatusCode)
            {
                _sawmill.Info($"VG Ban #{info.BanId} sent to Discord successfully");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _sawmill.Error($"Failed to send VG ban to Discord: {response.StatusCode} - {error}");
            }
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error sending VG ban to Discord: {ex.Message}");
        }
    }

    private WebhookPayload GeneratePayload(VgBanInfo info)
    {
        var color = info.BanType switch
        {
            "Server" => 0xFF0000u,
            "Role" => 0xFFA500u,
            "Department" => 0xFFEA00u,
            "Panel" => 0x9828C9u,
            _ => 0xFF0000u
        };

        var title = info.BanType switch
        {
            "Server" => $"🔨 Серверный бан #{info.BanId}",
            "Role" => $"⚔️ Ролевой бан #{info.BanId}",
            "Department" => $"🏢 Департаментный бан #{info.BanId}",
            "Panel" => $"📋 Панельный бан #{info.BanId}",
            _ => $"🔨 Бан #{info.BanId}"
        };

        var embed = new WebhookEmbed
        {
            Title = title,
            Color = color, // теперь uint
            Description = GenerateDescription(info),
            Footer = new WebhookEmbedFooter
            {
                Text = $"VG • {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC"
            },
            Timestamp = DateTimeOffset.UtcNow
        };

        return new WebhookPayload
        {
            Embeds = new List<WebhookEmbed> { embed }
        };
    }

    private string GenerateDescription(VgBanInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**Игрок:** {info.Target}");
        sb.AppendLine($"**Причина:** {info.Reason}");
        sb.AppendLine($"**Админ:** {info.AdminName ?? "System"}");

        if (info.Minutes > 0)
        {
            var duration = TimeSpan.FromMinutes(info.Minutes);
            sb.AppendLine($"**Длительность:** {duration.Days}д {duration.Hours}ч {duration.Minutes}м");
            if (info.Expires.HasValue)
                sb.AppendLine($"**Истекает:** {info.Expires.Value:yyyy-MM-dd HH:mm} UTC");
        }
        else
        {
            sb.AppendLine("**Длительность:** Перманентный");
        }

        if (info.AdditionalInfo.TryGetValue("role", out var role))
            sb.AppendLine($"**Роль:** {role}");
        if (info.AdditionalInfo.TryGetValue("department", out var department))
            sb.AppendLine($"**Департамент:** {department}");
        if (info.AdditionalInfo.TryGetValue("panelData", out var panelData))
            sb.AppendLine($"**Забанено:** {panelData}");

        return sb.ToString();
    }
}