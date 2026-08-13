using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.Economy.Prototypes;

/// <summary>
///     A tradable material that participates in the station market. The market
///     shift rule picks a handful of these commodities and adjusts their sell
///     multipliers up or down for a while.
/// </summary>
[Prototype]
public sealed partial class MarketCommodityPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     The <see cref="MaterialPrototype"/> this commodity tracks.
    /// </summary>
    [DataField(required: true)]
    public string Material = default!;

    /// <summary>
    ///     Sell multiplier applied when the commodity is in high demand.
    /// </summary>
    [DataField]
    public float HighDemandMultiplier = 1.8f;

    /// <summary>
    ///     Sell multiplier applied when the commodity is in low demand.
    /// </summary>
    [DataField]
    public float LowDemandMultiplier = 0.8f;
}
