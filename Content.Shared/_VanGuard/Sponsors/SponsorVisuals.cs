using Robust.Shared.Maths;

namespace Content.Shared._VanGuard.Sponsors;

public static class SponsorVisuals
{
    public const string Tier3Hex = "#00ffaa";
    public const string Tier2Hex = "#00a6b2";
    public const string Tier1Hex = "#cc0000";
    public const string NoTierHex = "#808080";

    public static Color GetColor(int tier) => tier switch
    {
        3 => Color.FromHex(Tier3Hex),
        2 => Color.FromHex(Tier2Hex),
        1 => Color.FromHex(Tier1Hex),
        _ => Color.FromHex(NoTierHex)
    };

    public static string GetHex(int tier) => tier switch
    {
        3 => Tier3Hex,
        2 => Tier2Hex,
        1 => Tier1Hex,
        _ => NoTierHex
    };
}
