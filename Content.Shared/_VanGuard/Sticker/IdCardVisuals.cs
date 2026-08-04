using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Sticker;

[Serializable, NetSerializable]
public enum IdCardVisuals : byte
{
    StickerOverlay,
    StickerColor
}