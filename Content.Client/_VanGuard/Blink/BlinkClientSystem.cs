using Content.Shared._VanGuard.Blink;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;

namespace Content.Client._VanGuard.Blink;

public sealed class BlinkClientSystem : EntitySystem
{
    private readonly Dictionary<EntityUid, Color> _originalEyeColors = new();
    private readonly Dictionary<EntityUid, int> _retryCount = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BlinkComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeLocalEvent<BlinkComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(EntityUid uid, BlinkComponent blink, ComponentShutdown args)
    {
        _originalEyeColors.Remove(uid);
        _retryCount.Remove(uid);
    }

    private void OnHandleState(EntityUid uid, BlinkComponent blink, ref AfterAutoHandleStateEvent args)
    {
        UpdateBlinkVisuals(uid, blink);
    }

    private void UpdateBlinkVisuals(EntityUid uid, BlinkComponent blink)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        if (!sprite.LayerMapTryGet(blink.EyeLayer, out var eyeLayerIndex))
            return;

        if (!sprite.LayerMapTryGet(HumanoidVisualLayers.Chest, out var skinLayerIndex))
            return;

        if (!_originalEyeColors.ContainsKey(uid))
        {
            if (!blink.EyesClosed)
            {
                var profileColor = GetEyeColorFromProfile(uid);
                
                if (profileColor != null && profileColor.Value != Color.White)
                {
                    _originalEyeColors[uid] = profileColor.Value;
                    sprite.LayerSetColor(eyeLayerIndex, profileColor.Value);
                    return;
                }

                var currentColor = sprite[eyeLayerIndex].Color;

                if (currentColor != Color.White && 
                    currentColor.R + currentColor.G + currentColor.B > 0.1f &&
                    currentColor.R + currentColor.G + currentColor.B < 2.9f)
                {
                    _originalEyeColors[uid] = currentColor;
                }
                else
                {
                    if (!_retryCount.ContainsKey(uid))
                        _retryCount[uid] = 0;
                    
                    _retryCount[uid]++;

                    if (_retryCount[uid] > 10)
                    {
                        _originalEyeColors[uid] = sprite[skinLayerIndex].Color;
                        _retryCount.Remove(uid);
                    }
                    else
                    {
                        return;
                    }
                }
            }
            else
            {
                return;
            }
        }

        if (blink.EyesClosed)
        {
            sprite.LayerSetColor(eyeLayerIndex, sprite[skinLayerIndex].Color);
        }
        else if (_originalEyeColors.TryGetValue(uid, out var originalColor))
        {
            sprite.LayerSetColor(eyeLayerIndex, originalColor);
        }
    }

    private Color? GetEyeColorFromProfile(EntityUid bodyUid)
    {
        var query = EntityQueryEnumerator<VisualOrganComponent, OrganComponent>();
        while (query.MoveNext(out var organUid, out var visualOrgan, out var organ))
        {
            if (organ.Body == bodyUid)
            {
                var categoryStr = organ.Category?.ToString() ?? "";
                if (categoryStr.Contains("Eyes") || 
                    categoryStr.Contains("Eye") ||
                    categoryStr.Equals("Head", StringComparison.OrdinalIgnoreCase))
                {
                    if (visualOrgan.Profile.EyeColor != Color.White)
                        return visualOrgan.Profile.EyeColor;
                }
            }
        }

        return null;
    }
}