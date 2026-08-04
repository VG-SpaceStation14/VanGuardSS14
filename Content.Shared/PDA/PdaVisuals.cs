using Robust.Shared.Serialization;

namespace Content.Shared.PDA
{
    [Serializable, NetSerializable]
    public enum PdaVisuals
    {
        IdCardInserted,
        PdaType,
        // VG-PDAScreens Start
        ScreenState,
        PenInserted
        // VG-PDAScreens End
    }

    [Serializable, NetSerializable]
    public enum PdaUiKey
    {
        Key
    }

}
