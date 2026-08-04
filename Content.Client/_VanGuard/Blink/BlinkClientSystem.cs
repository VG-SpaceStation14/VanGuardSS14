using Content.Shared._VanGuard.Blink;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;

namespace Content.Client._VanGuard.Blink;

public sealed class BlinkClientSystem : EntitySystem
{
    private readonly Dictionary<EntityUid, Color> _originalEyeColors = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BlinkComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeLocalEvent<BlinkComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(EntityUid uid, BlinkComponent blink, ComponentShutdown args)
    {
        _originalEyeColors.Remove(uid);
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

        if (!_originalEyeColors.ContainsKey(uid) && !blink.EyesClosed)
        {
            _originalEyeColors[uid] = sprite[eyeLayerIndex].Color;
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
}