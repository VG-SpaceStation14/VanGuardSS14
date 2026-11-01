using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Tools;

/// <summary>
/// Tells clients to animate a welding spark effect between two entities for the duration of a do-after.
/// </summary>
[Serializable, NetSerializable]
public sealed class SpawnedWeldingSparksEvent : EntityEventArgs
{
    public NetEntity ToolTarget { get; }
    public NetEntity SparkEffect { get; }
    public TimeSpan Lifetime { get; }

    public SpawnedWeldingSparksEvent(NetEntity toolTarget, NetEntity sparkEffect, TimeSpan lifetime)
    {
        ToolTarget = toolTarget;
        SparkEffect = sparkEffect;
        Lifetime = lifetime;
    }
}
