using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._VanGuard.PDA;

[Prototype]
public sealed partial class PdaWallpaperPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    [DataField("rsi")]
    public string RsiPath { get; private set; } = "/Textures/_VanGuard/Interface/pda/wallpapers.rsi";

    [DataField("state")]
    public string State { get; private set; } = string.Empty;
}