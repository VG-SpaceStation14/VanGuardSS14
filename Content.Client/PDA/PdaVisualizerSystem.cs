using Content.Shared.Light;
using Content.Shared.PDA;
using Robust.Client.GameObjects;
using Robust.Shared.Utility; // VG-Tweak

namespace Content.Client.PDA;

public sealed partial class PdaVisualizerSystem : VisualizerSystem<PdaVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, PdaVisualsComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (AppearanceSystem.TryGetData<string>(uid, PdaVisuals.PdaType, out var pdaType, args.Component))
            SpriteSystem.LayerSetRsiState((uid, args.Sprite), PdaVisualLayers.Base, pdaType);

        // VG-PDAScreens Start
        if (AppearanceSystem.TryGetData<SpriteSpecifier>(uid, PdaVisuals.ScreenState, out var screenState, args.Component))
            SpriteSystem.LayerSetSprite((uid, args.Sprite), PdaVisualLayers.Screen, screenState);
        // VG-PDAScreens End

        if (AppearanceSystem.TryGetData<bool>(uid, UnpoweredFlashlightVisuals.LightOn, out var isFlashlightOn, args.Component))
            SpriteSystem.LayerSetVisible((uid, args.Sprite), PdaVisualLayers.Flashlight, isFlashlightOn);

        if (AppearanceSystem.TryGetData<bool>(uid, PdaVisuals.IdCardInserted, out var isCardInserted, args.Component))
            SpriteSystem.LayerSetVisible((uid, args.Sprite), PdaVisualLayers.IdLight, isCardInserted);

        // VG-PDAScreens Start
        if (AppearanceSystem.TryGetData<bool>(uid, PdaVisuals.PenInserted, out var isPenInserted, args.Component))
            SpriteSystem.LayerSetVisible((uid, args.Sprite), PdaVisualLayers.Pen, isPenInserted);
        // VG-PDAScreens End
    }

    public enum PdaVisualLayers : byte
    {
        Base,
        Flashlight,
        IdLight,
        // VG-Tweak Start
        Screen,
        Pen
        // VG-Tweak End
    }
}
