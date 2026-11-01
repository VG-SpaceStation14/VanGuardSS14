using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Discord;

/// <summary>
/// Bridges role/department ban commands to the Discord webhook without making the command depend on
/// the webhook transport. All failures are contained here.
/// </summary>
public static class VgRoleBanHelper
{
    public static async Task SendRoleBanToDiscord<T>(
        NetUserId? target,
        string? targetUsername,
        NetUserId? banningAdmin,
        ProtoId<T> role,
        uint? minutes,
        string reason,
        DateTimeOffset? expires,
        string banType = "Role") where T : class, IPrototype
    {
        try
        {
            var sender = IoCManager.Resolve<VgBanWebhookSender>();
            var database = IoCManager.Resolve<IServerDbManager>();
            var players = IoCManager.Resolve<IPlayerManager>();

            var lastBan = await database.GetLastBanAsync();
            var nextId = (lastBan?.Id ?? 0) + 1;

            var admin = "System";
            if (banningAdmin is { } adminId && players.TryGetSessionById(adminId, out var session))
                admin = session.Name;

            await sender.SendBanAsync(new VgBanInfo
            {
                BanId = nextId.ToString(),
                Target = targetUsername ?? "Unknown",
                AdminName = admin,
                Reason = reason ?? string.Empty,
                Minutes = minutes ?? 0,
                Expires = expires,
                BanType = banType,
                AdditionalInfo = new Dictionary<string, string>
                {
                    ["role"] = role.ToString(),
                },
            });
        }
        catch (Exception ex)
        {
            Logger.GetSawmill("vg_role_ban").Error($"Role ban webhook failed: {ex.Message}");
        }
    }
}
