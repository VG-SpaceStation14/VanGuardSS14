using Content.Shared.Paper;
using Robust.Client.GameObjects;

using static Content.Shared.Paper.PaperComponent;

namespace Content.Client.Paper;

public sealed partial class PaperVisualizerSystem : VisualizerSystem<PaperVisualizerComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, PaperVisualizerComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (args.TryGetData<PaperStatus>(PaperVisuals.Status, out var writingStatus))
            SpriteSystem.LayerSetVisible((uid, args.Sprite), PaperVisualLayers.Writing, writingStatus == PaperStatus.Written);

        if (args.TryGetData<string>(PaperVisuals.Stamp, out var stampState))
        {
            if (stampState != string.Empty)
            {
                SpriteSystem.LayerSetRsiState((uid, args.Sprite), PaperVisualLayers.Stamp, stampState);
                SpriteSystem.LayerSetVisible((uid, args.Sprite), PaperVisualLayers.Stamp, true);
            }
            else
            {
                SpriteSystem.LayerSetVisible((uid, args.Sprite), PaperVisualLayers.Stamp, false);
            }

        }

        // VG-Tweak Start
        // Only tint the stamp layer if it actually exists (e.g. unstamped PaperWritten has no Stamp layer).
        if (SpriteSystem.LayerMapTryGet((uid, args.Sprite), PaperVisualLayers.Stamp, out _, false))
        {
            var tint = Color.White;
            if (AppearanceSystem.TryGetData<Color>(uid, PaperVisuals.StampTint, out var stampTint, args.Component))
                tint = stampTint;

            SpriteSystem.LayerSetColor((uid, args.Sprite), PaperVisualLayers.Stamp, tint);
        }
        // VG-Tweak End
    }
}

/// <summary>
/// Sprite mapping enum.
/// </summary>
public enum PaperVisualLayers
{
    Stamp,
    Writing,
}
