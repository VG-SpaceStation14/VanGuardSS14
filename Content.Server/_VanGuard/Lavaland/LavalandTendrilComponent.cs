using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Lavaland;

[RegisterComponent]
public sealed partial class LavalandTendrilComponent : Component
{
    [DataField]
    public List<EntProtoId> Spawns = new() { "MobWatcherLavaland" };

    [DataField]
    public int MaxSpawns = 2;

    [DataField]
    public float SpawnDelay = 12f;

    [DataField]
    public float ChasmDelay = 5f;

    [DataField]
    public int ChasmRadius = 1;

    [DataField]
    public EntProtoId Loot = "LavalandNecropolisCrate";

    [ViewVariables]
    public List<EntityUid> Mobs = new();

    public bool Destroyed;
}

[RegisterComponent]
public sealed partial class LavalandTendrilMobComponent : Component
{
    public EntityUid? Tendril;
}
