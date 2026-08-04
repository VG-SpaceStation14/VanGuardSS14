using System.Collections.Generic;
using System.Numerics;
using Content.Shared._VanGuard.Animation.Storage;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._VanGuard.Animation.Storage;

public sealed partial class ClosetInteractionAnimationSystem : EntitySystem
{
    [Dependency] private AnimationPlayerSystem _animation = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    private EntityQuery<SpriteComponent> _spriteQuery;
    private const float MinimumScale = 1.1f;
    private const string AnimationTrack = "closet-interaction-animation";

    private readonly Dictionary<EntityUid, bool> _previousOpenStates = new();
    private float _updateTimer;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClosetInteractionAnimationComponent, StorageAfterOpenEvent>(OnStorageAfterOpen);
        SubscribeLocalEvent<ClosetInteractionAnimationComponent, StorageAfterCloseEvent>(OnStorageAfterClose);

        _spriteQuery = GetEntityQuery<SpriteComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateTimer += frameTime;
        if (_updateTimer < 0.1f)
            return;
        _updateTimer = 0;

        var query = EntityQuery<ClosetInteractionAnimationComponent, AppearanceComponent, SpriteComponent>();
        foreach (var (animComp, appearance, sprite) in query)
        {
            var uid = animComp.Owner;

            if (!_appearance.TryGetData(uid, StorageVisuals.Open, out bool open, appearance))
                continue;

            if (_previousOpenStates.TryGetValue(uid, out var previousOpen))
            {
                if (open == previousOpen)
                    continue;
            }
            else
            {
                _previousOpenStates[uid] = open;
                continue;
            }

            if (!_animation.HasRunningAnimation(uid, AnimationTrack))
                DoAnimation(uid, sprite, animComp);

            _previousOpenStates[uid] = open;
        }
    }

    private void OnStorageAfterOpen(Entity<ClosetInteractionAnimationComponent> ent, ref StorageAfterOpenEvent args)
    {
        if (!_timing.IsFirstTimePredicted || IsClientSide(ent))
            return;

        TryPlayAnimation(ent);
    }

    private void OnStorageAfterClose(Entity<ClosetInteractionAnimationComponent> ent, ref StorageAfterCloseEvent args)
    {
        if (!_timing.IsFirstTimePredicted || IsClientSide(ent))
            return;

        TryPlayAnimation(ent);
    }

    private void TryPlayAnimation(Entity<ClosetInteractionAnimationComponent> ent)
    {
        if (!_spriteQuery.TryComp(ent, out var sprite))
            return;

        if (_animation.HasRunningAnimation(ent, AnimationTrack))
            return;

        DoAnimation(ent, sprite);
    }

    private void DoAnimation(Entity<ClosetInteractionAnimationComponent> ent, SpriteComponent sprite)
        => DoAnimation(ent, sprite, ent.Comp);

    private void DoAnimation(EntityUid uid, SpriteComponent sprite, ClosetInteractionAnimationComponent comp)
    {
        var targetScaleX = MathF.Max(comp.Scale + _random.NextFloat(-comp.ScaleVariation, comp.ScaleVariation), MinimumScale);
        var targetScaleY = MathF.Max(comp.Scale + _random.NextFloat(-comp.ScaleVariation, comp.ScaleVariation), MinimumScale);

        var animation = new Robust.Client.Animations.Animation
        {
            Length = TimeSpan.FromSeconds(comp.Duration),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    Property = nameof(SpriteComponent.Scale),
                    ComponentType = typeof(SpriteComponent),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(targetScaleX, targetScaleY), 0f),
                        new AnimationTrackProperty.KeyFrame(sprite.Scale, comp.Duration),
                    },
                },
            },
        };

        _animation.Play(uid, animation, AnimationTrack);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _previousOpenStates.Clear();
    }
}