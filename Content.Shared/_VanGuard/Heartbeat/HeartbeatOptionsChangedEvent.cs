using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Heartbeat;

[Serializable, NetSerializable]
public sealed class HeartbeatOptionsChangedEvent : EntityEventArgs
{
    public bool Enabled { get; }

    public HeartbeatOptionsChangedEvent(bool enabled)
    {
        Enabled = enabled;
    }
}