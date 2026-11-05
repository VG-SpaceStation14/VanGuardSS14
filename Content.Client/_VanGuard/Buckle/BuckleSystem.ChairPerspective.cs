using Content.Shared.Buckle.Components;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Client.Buckle;

internal sealed partial class BuckleSystem
{
    /// <summary>
    ///     Keeps chair occlusion in sync with the camera direction.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<StrapComponent>();
        while (query.MoveNext(out var uid, out var strap))
        {
            if (strap.BuckledEntities.Count == 0)
                continue;

            UpdateStrapDrawDepth((uid, strap));
        }
    }

    /// <summary>
    ///     Applies the camera-relative draw order for all entities buckled to a strap.
    /// </summary>
    private void UpdateStrapDrawDepth(Entity<StrapComponent> strap)
    {
        if (!strap.Comp.ModifyBuckleDrawDepth)
            return;

        if (!_spriteQuery.TryComp(strap, out var strapSprite))
            return;

        // North on screen means the chair's backrest is between the camera and
        // the buckled entity, so the chair must be drawn above it.
        var screenDir = (_xformSystem.GetWorldRotation(strap) + _eye.CurrentEye.Rotation).GetCardinalDir();
        var chairInFront = screenDir == Direction.North;

        foreach (var buckledEntity in strap.Comp.BuckledEntities)
        {
            UpdateBuckledEntityDrawDepth(strap, strapSprite, buckledEntity, chairInFront);
        }
    }

    /// <summary>
    ///     Applies the camera-relative draw order for one entity on a strap.
    /// </summary>
    private void UpdateBuckleDrawDepth(Entity<StrapComponent> strap, EntityUid buckledEntity)
    {
        if (!_spriteQuery.TryComp(strap, out var strapSprite))
            return;

        UpdateBuckledEntityDrawDepth(strap, strapSprite, buckledEntity);
    }

    /// <summary>
    ///     Applies the camera-relative draw order for a single buckled entity.
    /// </summary>
    /// <remarks>
    ///     Used on the buckle event, where the strap's buckled-entity list may not be updated yet.
    /// </remarks>
    private void UpdateBuckledEntityDrawDepth(
        Entity<StrapComponent> strap,
        SpriteComponent strapSprite,
        EntityUid buckledEntity,
        bool? chairInFront = null)
    {
        if (!strap.Comp.ModifyBuckleDrawDepth)
            return;

        if (!TryComp<BuckleComponent>(buckledEntity, out var buckle))
            return;

        if (!_spriteQuery.TryComp(buckledEntity, out var buckledSprite))
            return;

        chairInFront ??= (_xformSystem.GetWorldRotation(strap) + _eye.CurrentEye.Rotation)
            .GetCardinalDir() == Direction.North;

        if (chairInFront.Value)
        {
            buckle.OriginalDrawDepth ??= buckledSprite.DrawDepth;
            var targetDepth = strapSprite.DrawDepth - 1;
            if (buckledSprite.DrawDepth != targetDepth)
                _sprite.SetDrawDepth((buckledEntity, buckledSprite), targetDepth);
        }
        else if (buckle.OriginalDrawDepth.HasValue)
        {
            _sprite.SetDrawDepth((buckledEntity, buckledSprite), buckle.OriginalDrawDepth.Value);
            buckle.OriginalDrawDepth = null;
        }
    }
}
