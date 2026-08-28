using Content.Shared.AlertLevel;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility; // VG-Tweak

namespace Content.Shared.PDA
{
    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState] // VG-Tweak - AutoGenerateComponentState
    public sealed partial class PdaComponent : Component
    {
        public const string PdaIdSlotId = "PDA-id";
        public const string PdaPenSlotId = "PDA-pen";
        public const string PdaPaiSlotId = "PDA-pai";

        [DataField]
        public ItemSlot IdSlot = new();

        [DataField]
        public ItemSlot PenSlot = new();
        [DataField]
        public ItemSlot PaiSlot = new();

        // Really this should just be using ItemSlot.StartingItem. However, seeing as we have so many different starting
        // PDA's and no nice way to inherit the other fields from the ItemSlot data definition, this makes the yaml much
        // nicer to read.
        [DataField("id")]
        public EntProtoId? IdCard;

        // TODO: Fix persistence
        [ViewVariables] public EntityUid? ContainedId;
        [ViewVariables] public bool FlashlightOn;

        [ViewVariables(VVAccess.ReadWrite)] public string? OwnerName;
        // The Entity that "owns" the PDA, usually a player's character.
        // This is useful when we are doing stuff like renaming a player and want to find their PDA to change the name
        // as well.
        [ViewVariables(VVAccess.ReadWrite)] public EntityUid? PdaOwner;
        [ViewVariables] public string? StationName;
        [ViewVariables]
        public ProtoId<AlertLevelPrototype>? StationAlertLevel;
        [ViewVariables] public Color StationAlertColor = Color.White;

        // VG-Tweak Start: Настройка цвета обоев PDA
        [DataField]
        public bool HasWallpaperColor;

        [DataField]
        public Color WallpaperColor = Color.White;
        // VG-Tweak End

        // VG-Wallpaper Start
        [DataField, AutoNetworkedField]
        public string? WallpaperRsi { get; set; }

        [DataField, AutoNetworkedField]
        public string? WallpaperState { get; set; }
        // VG-Wallpaper End

        // VG-PDAScreens Start
        [DataField, AutoNetworkedField]
        public bool Powered = false;

        [DataField, AutoNetworkedField]
        public bool Booted = false;
        // VG-PDAScreens End
    }
}