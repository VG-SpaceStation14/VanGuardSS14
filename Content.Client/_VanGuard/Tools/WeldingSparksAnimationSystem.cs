using System.Numerics;
using Content.Shared._VanGuard.Tools;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._VanGuard.Tools;

public sealed partial class WeldingSparksAnimationSystem : EntitySystem
{
    [Dependency] private AnimationPlayerSystem _animation = default!;

    private const string AnimationKey = "WeldingSparksAnim";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SpawnedWeldingSparksEvent>(OnSpawnedEffect);
    }

    private void OnSpawnedEffect(SpawnedWeldingSparksEvent ev)
    {
        if (!TryGetEntity(ev.Target, out var target) || !TryGetEntity(ev.Sparks, out var sparks))
            return;

        if (!TryComp<WeldingSparksAnimationComponent>(target, out var animComp))
            return;

        if (!TryComp<SpriteComponent>(sparks, out var sprite))
            return;

        if (_animation.HasRunningAnimation(sparks.Value, AnimationKey))
            return;

        bool isWelded = false;

        var start = animComp.StartingOffset;
        var end = animComp.EndingOffset ?? -start;

        if (isWelded)
            (start, end) = (end, start);

        var animation = new Robust.Client.Animations.Animation
        {
            Length = ev.Duration,
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(start, 0f),
                        new AnimationTrackProperty.KeyFrame(end, (float) ev.Duration.TotalSeconds),
                    }
                }
            }
        };

        _animation.Play(sparks.Value, animation, AnimationKey);
    }
}