using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Heartbeat;

/// <summary>
/// Sent by a client to opt in or out of critical heartbeat audio.
/// </summary>
[Serializable, NetSerializable]
public sealed class HeartbeatOptionsChangedEvent : EntityEventArgs
{
    public bool IsEnabled { get; }

    public HeartbeatOptionsChangedEvent(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }
}
