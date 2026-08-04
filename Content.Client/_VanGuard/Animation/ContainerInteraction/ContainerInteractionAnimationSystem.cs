using System.Collections.Generic;
using System.Numerics;
using Content.Shared._VanGuard.Animation.ContainerInteraction;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._VanGuard.Animation.ContainerInteraction;

public sealed partial class ContainerInteractionAnimationSystem : EntitySystem
{
    [Dependency] private AnimationPlayerSystem _animation = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const float MinimumScale = 1.1f;
    private const string AnimationTrack = "container-interaction-animation";
    private static readonly TimeSpan Cooldown = TimeSpan.FromMilliseconds(300);

    private readonly Dictionary<EntityUid, TimeSpan> _lastAnimationTime = [];
    private readonly Dictionary<EntityUid, bool> _previousOpenStates = [];
    private readonly Dictionary<EntityUid, int> _previousCounts = [];

    private float _updateTimer;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ContainerInteractionAnimationComponent, EntInsertedIntoContainerMessage>(OnContainerInserted);
        SubscribeLocalEvent<ContainerInteractionAnimationComponent, EntRemovedFromContainerMessage>(OnContainerRemoved);
    }

    private void OnContainerInserted(Entity<ContainerInteractionAnimationComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!_timing.IsFirstTimePredicted || IsClientSide(ent))
            return;

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (!CanAnimate(ent))
            return;

        if (_animation.HasRunningAnimation(ent, AnimationTrack))
            return;

        _lastAnimationTime[ent] = _timing.CurTime;
        PlayBounceAnimation(ent, sprite);
    }

    private void OnContainerRemoved(Entity<ContainerInteractionAnimationComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!_timing.IsFirstTimePredicted || IsClientSide(ent))
            return;

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (!CanAnimate(ent))
            return;

        if (_animation.HasRunningAnimation(ent, AnimationTrack))
            return;

        _lastAnimationTime[ent] = _timing.CurTime;
        PlayBounceAnimation(ent, sprite);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateTimer += frameTime;
        if (_updateTimer < 0.1f)
            return;
        _updateTimer = 0;

        var query = EntityQuery<ContainerInteractionAnimationComponent, SpriteComponent>();
        foreach (var (animComp, sprite) in query)
        {
            var uid = animComp.Owner;

            if (TryComp<EntityStorageComponent>(uid, out var storage))
            {
                ProcessStorageState(uid, storage.Open, sprite, animComp);
                continue;
            }

            if (TryComp<StorageComponent>(uid, out var storageComp))
            {
                var currentCount = storageComp.StoredItems.Count;
                ProcessCountState(uid, currentCount, sprite, animComp);
            }
        }
    }

    private void ProcessStorageState(EntityUid uid, bool isOpen, SpriteComponent sprite, ContainerInteractionAnimationComponent comp)
    {
        if (!_previousOpenStates.TryGetValue(uid, out var previousOpen))
        {
            _previousOpenStates[uid] = isOpen;
            return;
        }

        if (isOpen == previousOpen)
            return;

        _previousOpenStates[uid] = isOpen;

        if (CanAnimate(uid) && !_animation.HasRunningAnimation(uid, AnimationTrack))
        {
            _lastAnimationTime[uid] = _timing.CurTime;
            PlayBounceAnimation(uid, sprite, comp);
        }
    }

    private void ProcessCountState(EntityUid uid, int currentCount, SpriteComponent sprite, ContainerInteractionAnimationComponent comp)
    {
        if (!_previousCounts.TryGetValue(uid, out var previousCount))
        {
            _previousCounts[uid] = currentCount;
            return;
        }

        if (currentCount == previousCount)
            return;

        _previousCounts[uid] = currentCount;

        if (CanAnimate(uid) && !_animation.HasRunningAnimation(uid, AnimationTrack))
        {
            _lastAnimationTime[uid] = _timing.CurTime;
            PlayBounceAnimation(uid, sprite, comp);
        }
    }

    private bool CanAnimate(EntityUid uid)
    {
        return !_lastAnimationTime.TryGetValue(uid, out var lastAnim) || 
               !(_timing.CurTime - lastAnim < Cooldown);
    }

    private void PlayBounceAnimation(Entity<ContainerInteractionAnimationComponent> ent, SpriteComponent sprite)
        => PlayBounceAnimation(ent, sprite, ent.Comp);

    private void PlayBounceAnimation(EntityUid uid, SpriteComponent sprite, ContainerInteractionAnimationComponent comp)
    {
        var randomOffset = _random.NextFloat(-comp.ScaleVariation, comp.ScaleVariation);
        var targetScaleX = MathF.Max(comp.Scale + randomOffset, MinimumScale);
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
        _previousCounts.Clear();
        _lastAnimationTime.Clear();
    }
}