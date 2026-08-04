using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared.PDA
{
    [Serializable, NetSerializable]
    public sealed class PdaUpdateState : CartridgeLoaderUiState
    {
        public bool FlashlightEnabled;
        public bool HasPen;
        public bool HasPai;
        public PdaIdInfoText PdaOwnerInfo;
        public string? StationName;
        public bool HasUplink;
        public bool CanPlayMusic;
        public string? Address;
        public bool HasWallpaperColor; // VG-Tweak
        public Color WallpaperColor; // VG-Tweak
        // VG-Wallpaper Start
        public string? WallpaperRsi;
        public string? WallpaperState;
        // VG-Wallpaper End
        public bool Booted; // VG-PDAScreens
        public bool Powered; // VG-PDAScreens

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
            bool hasWallpaperColor = false,
            Color? wallpaperColor = null,
            // VG-Wallpaper Start
            string? wallpaperRsi = null,
            string? wallpaperState = null,
            // VG-Wallpaper End
            bool booted = false,
            bool powered = true)
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
            HasWallpaperColor = hasWallpaperColor; // VG-Tweak
            WallpaperColor = wallpaperColor ?? Color.White; // VG-Tweak
            // VG-Wallpaper Start
            WallpaperRsi = wallpaperRsi;
            WallpaperState = wallpaperState;
            // VG-Wallpaper End
            Booted = booted; // VG-PDAScreens
            Powered = powered; // VG-PDAScreens
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