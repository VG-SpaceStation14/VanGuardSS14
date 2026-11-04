using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._VanGuard.Lavaland;

[RegisterComponent]
public sealed partial class LavalandPoiSpawnerComponent : Component
{
    [DataField]
    public List<EntProtoId> Prototypes = new() { "LavalandTendril" };

    [DataField]
    public float MinDistance = 100f;

    [DataField]
    public float MaxDistance = 220f;

    [DataField]
    public float ClearRadius = 18f;

    [DataField]
    public bool SendFax = true;

    [DataField]
    public float FaxDelay = 3f;

    [DataField]
    public List<ResPath> Dungeons = new();
}
