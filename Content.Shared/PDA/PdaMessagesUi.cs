using Robust.Shared.Serialization;

namespace Content.Shared.PDA;

[Serializable, NetSerializable]
public sealed class PdaToggleFlashlightMessage : BoundUserInterfaceMessage
{
    public PdaToggleFlashlightMessage() { }
}

[Serializable, NetSerializable]
public sealed class PdaShowRingtoneMessage : BoundUserInterfaceMessage
{
    public PdaShowRingtoneMessage() { }
}

[Serializable, NetSerializable]
public sealed class PdaShowUplinkMessage : BoundUserInterfaceMessage
{
    public PdaShowUplinkMessage() { }
}

[Serializable, NetSerializable]
public sealed class PdaLockUplinkMessage : BoundUserInterfaceMessage
{
    public PdaLockUplinkMessage() { }
}

[Serializable, NetSerializable]
public sealed class PdaShowMusicMessage : BoundUserInterfaceMessage
{
    public PdaShowMusicMessage() { }
}

[Serializable, NetSerializable]
public sealed class PdaRequestUpdateInterfaceMessage : BoundUserInterfaceMessage
{
    public PdaRequestUpdateInterfaceMessage() { }
}

// VG-Tweak Start
[Serializable, NetSerializable]
public sealed class PdaPowerOffMessage : BoundUserInterfaceMessage
{
    public PdaPowerOffMessage() { }
}

[Serializable, NetSerializable]
public sealed class PdaBootFinishedMessage : BoundUserInterfaceMessage
{
    public PdaBootFinishedMessage() { }
}

[Serializable, NetSerializable]
public sealed class PdaSetWallpaperColorMessage : BoundUserInterfaceMessage
{
    public Color Color;

    public PdaSetWallpaperColorMessage(Color color)
    {
        Color = color;
    }
}
// VG-Tweak End