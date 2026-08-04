using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Tools;

[Serializable, NetSerializable]
public sealed class SpawnedWeldingSparksEvent : EntityEventArgs
{
    public NetEntity Target { get; }
    public NetEntity Sparks { get; }
    public TimeSpan Duration { get; }

    public SpawnedWeldingSparksEvent(NetEntity target, NetEntity sparks, TimeSpan duration)
    {
        Target = target;
        Sparks = sparks;
        Duration = duration;
    }
}