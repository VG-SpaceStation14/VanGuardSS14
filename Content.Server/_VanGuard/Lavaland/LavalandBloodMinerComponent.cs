namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Second-phase Blood Miner behaviour that is not covered by the generic HTN
/// tasks: periodic short-range teleports around its current target.
/// </summary>
[RegisterComponent]
public sealed partial class LavalandBloodMinerComponent : Component
{
    [DataField]
    public float TeleportInterval = 6f;

    [ViewVariables]
    public TimeSpan NextTeleport;
}
