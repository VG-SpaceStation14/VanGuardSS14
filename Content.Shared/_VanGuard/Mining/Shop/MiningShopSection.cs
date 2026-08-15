using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Mining.Shop;

/// <summary>
/// A section of the mining shop UI, defined as a prototype so entries can be added in YAML.
/// </summary>
[Prototype]
public sealed partial class MiningShopSectionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Localized name of the section shown in the shop UI.
    /// </summary>
    [DataField(required: true)]
    public LocId LocId;

    /// <summary>
    /// Optional hardcoded fallback name used when the localized key cannot be resolved.
    /// </summary>
    [DataField]
    public string? Name;

    [DataField(required: true)]
    public List<MiningShopEntry> Entries = new();
}

/// <summary>
/// A single purchasable entry in the mining shop.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial record MiningShopEntry
{
    [DataField(required: true)]
    public EntProtoId Id;

    [DataField]
    public string? Name;

    [DataField]
    public uint? Price;
}
