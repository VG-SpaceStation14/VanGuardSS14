using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Lavaland.Components;

[RegisterComponent]
public sealed partial class LavalandOreDepositComponent : Component
{
    [DataField]
    public List<EntProtoId> Ores = new()
    {
        "AsteroidRockCoal",
        "AsteroidRockGold",
        "AsteroidRockSilver",
        "AsteroidRockPlasma",
        "AsteroidRockUranium",
        "AsteroidRockQuartz",
        "AsteroidRockDiamond",
    };
}
