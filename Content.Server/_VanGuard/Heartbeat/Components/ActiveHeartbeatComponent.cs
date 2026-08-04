namespace Content.Server._VanGuard.Heartbeat.Components;

[RegisterComponent]
public sealed partial class ActiveHeartbeatComponent : Component
{
    [DataField]
    public float Pitch = 1f;

    [DataField]
    public TimeSpan NextHeartbeatCooldown = TimeSpan.FromSeconds(1f);

    [DataField]
    public TimeSpan NextHeartbeatTime;
}