using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared.Roles;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Server.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Discord;

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
            var vgSender = IoCManager.Resolve<VgBanWebhookSender>();
            if (vgSender == null)
                return;

            var dbManager = IoCManager.Resolve<IServerDbManager>();
            var lastBan = await dbManager.GetLastBanAsync();
            var newBanId = lastBan is not null ? lastBan.Id + 1 : 1;

            string adminName = "System";
            if (banningAdmin.HasValue)
            {
                var playerManager = IoCManager.Resolve<IPlayerManager>();
                if (playerManager.TryGetSessionById(banningAdmin.Value, out var adminSession))
                    adminName = adminSession.Name;
            }

            var vgBanInfo = new VgBanInfo
            {
                BanId = newBanId.ToString() ?? "0",
                Target = targetUsername ?? "Unknown",
                AdminName = adminName,
                Reason = reason ?? string.Empty,
                Minutes = minutes ?? 0,
                Expires = expires,
                BanType = banType,
                AdditionalInfo = new Dictionary<string, string>
                {
                    ["role"] = role.ToString()
                }
            };

            await vgSender.SendBanAsync(vgBanInfo);
        }
        catch (Exception ex)
        {
            var sawmill = Logger.GetSawmill("vg_role_ban");
            sawmill.Error($"Failed to send role ban to Discord: {ex.Message}");
        }
    }
}