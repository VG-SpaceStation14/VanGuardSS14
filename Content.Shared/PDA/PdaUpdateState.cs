using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared.PDA
{
    [Serializable, NetSerializable]
    public sealed class PdaUpdateState : CartridgeLoaderUiState // WTF is this. what. I ... fuck me I just want net entities to work
        // TODO purge this shit
        //AAAAAAAAAAAAAAAA
    {
        public bool FlashlightEnabled;
        public bool HasPen;
        public bool HasPai;
        public PdaIdInfoText PdaOwnerInfo;
        public string? StationName;
        public bool HasUplink;
        public bool CanPlayMusic;
        public string? Address;
        // VG-PDAScreens Start
        public bool Booted;
        public bool Powered;
        // VG-PDAScreens End

        public PdaUpdateState(
            List<NetEntity> programs,
            NetEntity? activeUI,
            bool flashlightEnabled,
            bool hasPen,
            bool hasPai,
            PdaIdInfoText pdaOwnerInfo,
            string? stationName,
            bool hasUplink = false,
            bool canPlayMusic = false,
            string? address = null,
            // VG-PDAScreens Start
            bool booted = false,
            bool powered = true)
            // VG-PDAScreens End
            : base(programs, activeUI)
        {
            FlashlightEnabled = flashlightEnabled;
            HasPen = hasPen;
            HasPai = hasPai;
            PdaOwnerInfo = pdaOwnerInfo;
            HasUplink = hasUplink;
            CanPlayMusic = canPlayMusic;
            StationName = stationName;
            Address = address;
            // VG-PDAScreens Start
            Booted = booted;
            Powered = powered;
            // VG-PDAScreens End
        }
    }

    [Serializable, NetSerializable]
    public struct PdaIdInfoText
    {
        public string? ActualOwnerName;
        public string? IdOwner;
        public string? JobTitle;
        public string? StationAlertLevel;
        public Color StationAlertColor;
    }
}
