using Content.Client.IconSmoothing;
using Content.Shared.Chemistry.Components;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Map;

namespace Content.Client.Fluids;

public sealed partial class PuddleSystem : SharedPuddleSystem
{
    [Dependency] private IconSmoothSystem _smooth = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    [SubscribeLocalEvent]
    private void OnPuddleAppearance(EntityUid uid, PuddleComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!args.TryGetData<float>(PuddleVisuals.CurrentVolume, out var volume))
            volume = 1.0f;

        // VG-Tweak Start: puddles like Footstep/DragMark have no visual PuddleKey layer
        // (their default PuddleKey is "direction", but the sprite has no layers at all).
        // Bail out early so LayerSetRsiState doesn't spam "Layer with key 'direction' does not exist".
        if (!_sprite.LayerMapTryGet((uid, args.Sprite), component.PuddleKey, out var puddleLayer, logMissing: false))
            return;
        // VG-Tweak End

        // VG-Tweak Start: not every PuddleComponent has an IconSmoothComponent
        // (Footstep, DragMark, custom micro-puddles). Cache it so SetEnabled
        // can receive (uid, smooth) instead of just uid and skip the Resolve error.
        TryComp<IconSmoothComponent>(uid, out var smooth);
        // VG-Tweak End

        // Update smoothing and sprite based on volume.
        if (volume < LowThreshold)
        {
            // VG-Tweak: use cached layer index instead of re-resolving by string key
            _sprite.LayerSetRsiState((uid, args.Sprite), puddleLayer, $"{component.PuddleState}a");
            // VG-Tweak: pass (uid, smooth) instead of uid so Resolve is skipped on entities without the component
            if (smooth != null)
                _smooth.SetEnabled((uid, smooth), false, false);
        }
        else if (volume < MediumThreshold)
        {
            // VG-Tweak: use cached layer index instead of re-resolving by string key
            _sprite.LayerSetRsiState((uid, args.Sprite), puddleLayer, $"{component.PuddleState}b");
            // VG-Tweak: pass (uid, smooth) instead of uid so Resolve is skipped on entities without the component
            if (smooth != null)
                _smooth.SetEnabled((uid, smooth), false, false);
        }
        else
        {
            // VG-Tweak: pass (uid, smooth) instead of uid so Resolve is skipped on entities without the component
            if (smooth != null)
                _smooth.SetEnabled((uid, smooth), true);
        }

        var baseColor = Color.White;

        if (!args.TryGetData<Color>(PuddleVisuals.SolutionColor, out var color))
            color = args.Sprite.Color;

        _sprite.SetColor((uid, args.Sprite), color * baseColor);
    }

    #region Spill

    // Maybe someday we'll have clientside prediction for entity spawning, but not today.
    // Until then, these methods do nothing on the client.
    /// <inheritdoc/>
    public override bool TrySplashSpillAt(Entity<SpillableComponent?> entity, EntityCoordinates coordinates, out EntityUid puddleUid, out Solution solution, bool sound = true, EntityUid? user = null)
    {
        puddleUid = EntityUid.Invalid;
        solution = new Solution();
        return false;
    }

    public override bool TrySplashSpillAt(EntityUid entity,
        EntityCoordinates coordinates,
        Solution spilled,
        out EntityUid puddleUid,
        bool sound = true,
        EntityUid? user = null)
    {
        puddleUid = EntityUid.Invalid;
        return false;
    }

    /// <inheritdoc/>
    public override bool TrySpillAt(EntityCoordinates coordinates, Solution solution, out EntityUid puddleUid, bool sound = true)
    {
        puddleUid = EntityUid.Invalid;
        return false;
    }

    /// <inheritdoc/>
    public override bool TrySpillAt(EntityUid uid, Solution solution, out EntityUid puddleUid, bool sound = true, TransformComponent? transformComponent = null)
    {
        puddleUid = EntityUid.Invalid;
        return false;
    }

    /// <inheritdoc/>
    public override bool TrySpillAt(TileRef tileRef, Solution solution, out EntityUid puddleUid, bool sound = true, bool tileReact = true)
    {
        puddleUid = EntityUid.Invalid;
        return false;
    }

    #endregion Spill
}
