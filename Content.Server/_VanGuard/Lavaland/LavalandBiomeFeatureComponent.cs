using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Lavaland;

[RegisterComponent]
public sealed partial class LavalandBiomeFeatureComponent : Component
{
    [DataField]
    public List<EntProtoId> Options = new()
    {
        "LavalandTendril",
        "FloorChasmEntity",
        "FloraRockSolid",
        "MobWatcherLavaland",
    };
}
