using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.Delivery.Components;

/// <summary>
/// Falling supply pod. Contents stay inside until the pod lands, then the
/// opened-pod visual is spawned and the contents are released exactly once.
/// </summary>
[RegisterComponent]
public sealed partial class DeliveryPodComponent : Component
{
    public const string ContainerId = "delivery";

    /// <summary>
    /// Lifetime of the falling capsule.
    /// </summary>
    [DataField]
    public TimeSpan LandingDelay = TimeSpan.FromSeconds(5.1);

    /// <summary>
    /// The opened pod/crater entity spawned on landing.
    /// </summary>
    [DataField]
    public EntProtoId OpenedPrototype = "DeliveryPodOpened";

    public Container Contents = default!;

    /// <summary>
    /// Original item prototypes, used to recover goods if the pod is destroyed early.
    /// </summary>
    public List<EntProtoId> ContentsPrototypes = new();

    public TimeSpan LandAt;

    public bool Landed;

    public bool OpenedSpawned;

    public bool Released;
}
