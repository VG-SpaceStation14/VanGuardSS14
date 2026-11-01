namespace Content.Server._VanGuard.Heartbeat.Components;

/// <summary>
/// Marks a mob that can produce a critical-state heartbeat. The timer fields are runtime state and
/// intentionally are not serialized: they are recomputed whenever the mob enters critical condition.
/// </summary>
[RegisterComponent]
public sealed partial class CritHeartbeatComponent : Component
{
    public float Pitch = 1f;
    public TimeSpan Interval = TimeSpan.FromSeconds(1);
    public TimeSpan NextBeat;
}
