using Robust.Shared.Configuration;

namespace Content.Shared._VanGuard.CCVars;

[CVarDefs]
public sealed class VGCCVars
{
    /// <summary>
    ///     Detailed_Examine enable
    /// </summary>
    public static readonly CVarDef<bool> DetailedExamine =
        CVarDef.Create("vg.detailed_examine", true, CVar.CLIENT | CVar.ARCHIVE);

    /// <summary>
    ///     Discord ban webhook link
    /// </summary>
    public static readonly CVarDef<string> DiscordBanWebhook =
        CVarDef.Create("vg.discord.ban_webhook", string.Empty, CVar.SERVERONLY);
    
    /// <summary>
    ///     Discord ban webhook enable
    /// </summary>
    public static readonly CVarDef<bool> DiscordBanWebhookEnabled =
        CVarDef.Create("vg.discord.ban_webhook_enabled", true, CVar.SERVERONLY);
}