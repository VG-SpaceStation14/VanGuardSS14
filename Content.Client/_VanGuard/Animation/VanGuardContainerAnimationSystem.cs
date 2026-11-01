using System.Collections.Generic;
using System.Numerics;
using Content.Shared._VanGuard.Animation.ContainerInteraction;
using Content.Shared._VanGuard.Animation.Storage;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._VanGuard.Animation;

/// <summary>
/// Gives storage entities a short scale "pop" when their contents change or a closet is opened/closed.
/// The animation is entirely client-side and is rate-limited per entity so rapid transfers do not restart it.
/// </summary>
public sealed partial class VanGuardContainerAnimationSystem : EntitySystem
{
    private const float MinScale = 1.1f;
    private const string Track = "vanguard-container-pop";
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(300);

    [Dependency] private AnimationPlayerSystem _animation = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _lastPlayed = new();
    private readonly Dictionary<EntityUid, bool> _closetOpen = new();
    private readonly Dictionary<EntityUid, int> _itemCounts = new();
    private float _scanAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ContainerInteractionAnimationComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<ContainerInteractionAnimationComponent, EntRemovedFromContainerMessage>(OnRemoved);

        SubscribeLocalEvent<ClosetInteractionAnimationComponent, StorageAfterOpenEvent>(OnStorageOpened);
        SubscribeLocalEvent<ClosetInteractionAnimationComponent, StorageAfterCloseEvent>(OnStorageClosed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _scanAccumulator += frameTime;
        if (_scanAccumulator < 0.1f)
            return;

        _scanAccumulator = 0f;

        // StorageComponent has no open/close event of its own, so bags and boxes are checked by item count.
        var bags = EntityQueryEnumerator<ContainerInteractionAnimationComponent, StorageComponent>();
        while (bags.MoveNext(out var uid, out var anim, out var storage))
        {
            var count = storage.StoredItems.Count;
            if (!_itemCounts.TryGetValue(uid, out var known))
            {
                _itemCounts[uid] = count;
                continue;
            }

            if (known == count)
                continue;

            _itemCounts[uid] = count;
            TryAnimate(uid, anim.Scale, anim.ScaleVariation, anim.Duration);
        }

        // Fallback for clients that do not predict the shared open/close event.
        var closets = EntityQueryEnumerator<ClosetInteractionAnimationComponent, AppearanceComponent>();
        while (closets.MoveNext(out var uid, out var anim, out var appearance))
        {
            if (!_appearance.TryGetData(uid, StorageVisuals.Open, out bool open, appearance))
                continue;

            if (!_closetOpen.TryGetValue(uid, out var previous))
            {
                _closetOpen[uid] = open;
                continue;
            }

            if (previous == open)
                continue;

            _closetOpen[uid] = open;
            TryAnimate(uid, anim.Scale, anim.ScaleVariation, anim.Duration);
        }

        CleanupTrackedEntities();
    }

    private void OnInserted(Entity<ContainerInteractionAnimationComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        TryAnimate(ent, ent.Comp.Scale, ent.Comp.ScaleVariation, ent.Comp.Duration);
    }

    private void OnRemoved(Entity<ContainerInteractionAnimationComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        TryAnimate(ent, ent.Comp.Scale, ent.Comp.ScaleVariation, ent.Comp.Duration);
    }

    private void OnStorageOpened(Entity<ClosetInteractionAnimationComponent> ent, ref StorageAfterOpenEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        TryAnimate(ent, ent.Comp.Scale, ent.Comp.ScaleVariation, ent.Comp.Duration);
    }

    private void OnStorageClosed(Entity<ClosetInteractionAnimationComponent> ent, ref StorageAfterCloseEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        TryAnimate(ent, ent.Comp.Scale, ent.Comp.ScaleVariation, ent.Comp.Duration);
    }

    private void TryAnimate(EntityUid uid, float scale, float variation, float duration)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        if (_animation.HasRunningAnimation(uid, Track))
            return;

        if (_lastPlayed.TryGetValue(uid, out var last) && _timing.CurTime - last < RepeatDelay)
            return;

        _lastPlayed[uid] = _timing.CurTime;
        PlayPop(uid, sprite, scale, variation, duration);
    }

    private void PlayPop(EntityUid uid, SpriteComponent sprite, float baseScale, float variation, float duration)
    {
        var width = MathF.Max(baseScale + _random.NextFloat(-variation, variation), MinScale);
        var height = MathF.Max(baseScale + _random.NextFloat(-variation, variation), MinScale);

        var animation = new Robust.Client.Animations.Animation
        {
            Length = TimeSpan.FromSeconds(duration),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(width, height), 0f),
                        new AnimationTrackProperty.KeyFrame(sprite.Scale, duration),
                    },
                },
            },
        };

        _animation.Play(uid, animation, Track);
    }

    private void CleanupTrackedEntities()
    {
        if (_lastPlayed.Count == 0 && _itemCounts.Count == 0 && _closetOpen.Count == 0)
            return;

        List<EntityUid>? gone = null;
        foreach (var uid in _lastPlayed.Keys)
        {
            if (!Exists(uid))
                (gone ??= new List<EntityUid>()).Add(uid);
        }

        if (gone != null)
        {
            foreach (var uid in gone)
                _lastPlayed.Remove(uid);
        }

        gone = null;
        foreach (var uid in _itemCounts.Keys)
        {
            if (!Exists(uid))
                (gone ??= new List<EntityUid>()).Add(uid);
        }

        if (gone == null)
            return;

        foreach (var uid in gone)
            _itemCounts.Remove(uid);

        gone = null;
        foreach (var uid in _closetOpen.Keys)
        {
            if (!Exists(uid))
                (gone ??= new List<EntityUid>()).Add(uid);
        }

        if (gone == null)
            return;

        foreach (var uid in gone)
            _closetOpen.Remove(uid);
    }
}
