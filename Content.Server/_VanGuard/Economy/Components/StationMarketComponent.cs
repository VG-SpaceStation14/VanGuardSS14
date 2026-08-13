using Robust.Shared.Serialization;

namespace Content.Server._VanGuard.Economy.Components;

/// <summary>
///     Tracks the current market state of a station: per-material sell multipliers
///     applied when cargo sells goods produced by that station.
/// </summary>
[RegisterComponent]
public sealed partial class StationMarketComponent : Component
{
    /// <summary>
    ///     Material id to sell multiplier. Absent materials sell at their base price.
    /// </summary>
    [DataField]
    public Dictionary<string, float> MaterialMultipliers = new();

    /// <summary>
    ///     A short log of the most recent market changes for diagnostics.
    /// </summary>
    [DataField]
    public List<MarketChangeSnapshot> RecentChanges = new();

    [DataField]
    public int MaxRecentChanges = 20;

    [DataField]
    public int ChangeSequence;
}

[Serializable]
public sealed record MarketChangeSnapshot(string Material, float Multiplier, int Sequence);
