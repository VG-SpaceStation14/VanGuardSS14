using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Sticker;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StickerComponent : Component
{
    [DataField(required: true)]
    [AutoNetworkedField]
    public string OverlayState = string.Empty;

    [DataField]
    [AutoNetworkedField]
    public Color? Color;
}