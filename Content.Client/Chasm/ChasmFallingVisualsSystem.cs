// VG-Tweak Start
using System.Numerics;
// VG-Tweak End
using Content.Shared.Chasm;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Random;

namespace Content.Client.Chasm;

/// <summary>
/// Handles the falling animation for entities that fall into an entity with <see cref="ChasmComponent"/>.
/// </summary>
public sealed partial class ChasmFallingVisualsSystem : EntitySystem
{
    [Dependency] private AnimationPlayerSystem _anim = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    // VG-Tweak Start
    // Own VanGuardSS14 change: pick a fresh spin for every fall so the
    // animation never looks identical twice.
    [Dependency] private IRobustRandom _random = default!;
    // VG-Tweak End

    // VG-Tweak Start
    // Remembers the sprite rotation for the duration of the animation so it
    // can be restored if the fall is cancelled before the entity is deleted.
    private readonly Dictionary<EntityUid, Angle> _originalRotations = new();
    // VG-Tweak End

    [Dependency] private EntityQuery<AnimationPlayerComponent> _animationPlayerQuery;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery;

    private const string ChasmFallAnimationKey = "chasm_fall";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChasmFallingComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<ChasmFallingComponent, ComponentRemove>(OnComponentRemove);
    }

    private void OnComponentInit(Entity<ChasmFallingComponent> entity, ref ComponentInit args)
    {
        if (!_spriteQuery.TryComp(entity, out var sprite) ||
            TerminatingOrDeleted(entity))
        {
            return;
        }

        entity.Comp.OriginalScale = sprite.Scale;
        // VG-Tweak Start
        // The falling component is networked from the server, where
        // OriginalScale has never been set; avoid starting the animation from
        // a zero scale when that state arrives after ComponentInit.
        entity.Comp.OriginalScale = SanitizeScale(entity.Comp.OriginalScale);
        // VG-Tweak End

        // VG-Tweak Start
        _originalRotations[entity] = sprite.Rotation;
        // VG-Tweak End

        if (!_animationPlayerQuery.TryComp(entity, out var player) ||
            _anim.HasRunningAnimation(player, ChasmFallAnimationKey))
        {
            return;
        }

        _anim.Play((entity, player), GetFallingAnimation(entity.Owner, entity.Comp), ChasmFallAnimationKey);
    }

    private void OnComponentRemove(Entity<ChasmFallingComponent> entity, ref ComponentRemove args)
    {
        // VG-Tweak Start
        // Restore the original rotation first, then fall through to the
        // generic sprite handling below; the dictionary entry is dropped in
        // every case, including the early returns.
        if (_originalRotations.Remove(entity, out var originalRotation) &&
            _spriteQuery.TryComp(entity, out var rotatedSprite))
        {
            _sprite.SetRotation((entity, rotatedSprite), originalRotation);
        }
        // VG-Tweak End

        if (!_spriteQuery.TryComp(entity, out var sprite))
        {
            return;
        }

        // VG-Tweak Start
        _sprite.SetScale((entity, sprite), SanitizeScale(entity.Comp.OriginalScale));
        // VG-Tweak End

        if (!_animationPlayerQuery.TryComp(entity, out var player) ||
            !_anim.HasRunningAnimation(player, ChasmFallAnimationKey))
        {
            return;
        }

        _anim.Stop((entity, player), ChasmFallAnimationKey);
    }

    private Animation GetFallingAnimation(EntityUid uid, ChasmFallingComponent component)
    {
        // VG-Tweak Start
        var originalScale = SanitizeScale(component.OriginalScale);
        var originalRotation = _originalRotations.GetValueOrDefault(uid, Angle.Zero);
        // Falling into the chasm is a "swirl down the shaft": the sprite is
        // spun by one to two full turns in a random direction, as if the pit
        // were pulling it in. Both direction and magnitude are rolled per fall.
        var direction = _random.Next(2) == 0 ? 1f : -1f;
        var totalRotation = _random.NextFloat(360f, 720f) * direction;
        // VG-Tweak End
        return new Animation
        {
            Length = component.AnimationTime,
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(originalScale, 0.0f),
                        new AnimationTrackProperty.KeyFrame(component.AnimationScale, component.AnimationTime.Seconds),
                    },
                    InterpolationMode = AnimationInterpolationMode.Cubic,
                },
                // VG-Tweak Start
                // Own VanGuardSS14 change: spin while shrinking so the fall
                // reads as a tumble down the shaft, not just a zoom-out.
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Rotation),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(originalRotation, 0.0f),
                        new AnimationTrackProperty.KeyFrame(originalRotation + Angle.FromDegrees(totalRotation), component.AnimationTime.Seconds),
                    },
                    InterpolationMode = AnimationInterpolationMode.Linear,
                },
                // VG-Tweak End
            },
        };
    }

    // VG-Tweak Start
    private static Vector2 SanitizeScale(Vector2 scale)
    {
        if (MathF.Abs(scale.X) < 0.005f || MathF.Abs(scale.Y) < 0.005f)
            return Vector2.One;

        return scale;
    }
    // VG-Tweak End
}
