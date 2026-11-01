using System.Numerics;
using Content.Shared._VanGuard.Tools;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._VanGuard.Tools;

/// <summary>
/// Animates a welding spark sprite along the configured seam while the server-side do-after runs.
/// </summary>
public sealed partial class WeldingSparksAnimationSystem : EntitySystem
{
    [Dependency] private AnimationPlayerSystem _animation = default!;

    private const string Track = "VGWeldingSparks";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SpawnedWeldingSparksEvent>(OnSparkStarted);
    }

    private void OnSparkStarted(SpawnedWeldingSparksEvent args)
    {
        if (!TryGetEntity(args.ToolTarget, out var target)
            || !TryGetEntity(args.SparkEffect, out var spark)
            || !TryComp<WeldingSparksAnimationComponent>(target, out var offsets)
            || !TryComp<SpriteComponent>(spark, out _))
            return;

        if (_animation.HasRunningAnimation(spark.Value, Track))
            return;

        var from = offsets.StartingOffset;
        var to = offsets.EndingOffset ?? -from;

        var animation = new Robust.Client.Animations.Animation
        {
            Length = args.Lifetime,
            AnimationTracks =
            {
                StartOffsetTrack(from, to, (float) args.Lifetime.TotalSeconds),
            },
        };

        _animation.Play(spark.Value, animation, Track);
    }

    private static AnimationTrackComponentProperty StartOffsetTrack(Vector2 from, Vector2 to, float duration)
    {
        return new AnimationTrackComponentProperty
        {
            ComponentType = typeof(SpriteComponent),
            Property = nameof(SpriteComponent.Offset),
            InterpolationMode = AnimationInterpolationMode.Linear,
            KeyFrames =
            {
                new AnimationTrackProperty.KeyFrame(from, 0f),
                new AnimationTrackProperty.KeyFrame(to, duration),
            },
        };
    }
}
