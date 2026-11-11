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

    // Light bloom
    /// <summary>
    ///     Bloom (glow) effect enable. When disabled, no glow/bloom effect is rendered.
    /// </summary>
    public static readonly CVarDef<bool> BloomEnabled =
        CVarDef.Create("vg.bloom_enabled", true, CVar.CLIENT | CVar.ARCHIVE);

    /// <summary>
    ///     Enable the cone-shaped light glow (flashlights).
    /// </summary>
    public static readonly CVarDef<bool> LightBloomConeEnable =
        CVarDef.Create("vg.light_bloom_cone_enable", false, CVar.CLIENT | CVar.ARCHIVE);

    /// <summary>
    ///     Strength of the light bloom glow.
    /// </summary>
    public static readonly CVarDef<float> LightBloomStrength =
        CVarDef.Create("vg.light_bloom_strength", 0.1f, CVar.CLIENT | CVar.ARCHIVE);

    /// <summary>
    ///     Strength of the volumetric light glow.
    /// </summary>
    public static readonly CVarDef<float> VolumetricLightStrength =
        CVarDef.Create("vg.volumetric_light_strength", 0.001f, CVar.CLIENT | CVar.ARCHIVE);

    /// <summary>
    ///     Should the client render inline sprites (icons) in pointing chat messages?
    /// </summary>
    public static readonly CVarDef<bool> PointingChatSprites =
        CVarDef.Create("vg.pointing_chat_sprites", true, CVar.CLIENT | CVar.ARCHIVE);

    /// <summary>
    ///     Enables the automatic round-start Lavaland bootstrap. Disabled by default
    ///     for lightweight/test servers; VanGuard's common config enables it.
    /// </summary>
    public static readonly CVarDef<bool> LavalandRoundstart =
        CVarDef.Create("vg.lavaland_roundstart", false, CVar.SERVERONLY);

    /// <summary>
    /// Link to the Discord server for binding.
    /// </summary>
    public static readonly CVarDef<string> DiscordInviteUrl =
        CVarDef.Create("vg.discord.invite_url", "https://discord.gg/nW73CdBYkg", CVar.SERVER | CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Secret token for communication with the Python bot.
    /// </summary>
    public static readonly CVarDef<string> DiscordBotSecret =
        CVarDef.Create("vg.discord.bot_secret", "CHANGE_THIS_TO_A_RANDOM_SECRET_STRING", CVar.SERVERONLY | CVar.CONFIDENTIAL);
}
