using Content.Shared.Access.Components;
using Content.Shared._VanGuard.Sticker;
using Robust.Client.GameObjects;

namespace Content.Client._VanGuard.Sticker;

public sealed class StickerVisualizerSystem : VisualizerSystem<IdCardComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, IdCardComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!args.Sprite.LayerMapTryGet("Sticker", out var layerIndex))
            return;

        if (AppearanceSystem.TryGetData<string>(uid, IdCardVisuals.StickerOverlay, out var overlay, args.Component)
            && !string.IsNullOrEmpty(overlay))
        {
            args.Sprite.LayerSetState(layerIndex, overlay);
            args.Sprite.LayerSetVisible(layerIndex, true);
        }
        else
        {
            args.Sprite.LayerSetVisible(layerIndex, false);
        }
    }
}