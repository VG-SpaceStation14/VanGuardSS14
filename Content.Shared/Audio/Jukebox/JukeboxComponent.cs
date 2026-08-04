using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Audio.Jukebox;

[NetworkedComponent, RegisterComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedJukeboxSystem))]
public sealed partial class JukeboxComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<JukeboxPrototype>? SelectedSongId;

    [DataField, AutoNetworkedField]
    public EntityUid? AudioStream;

    /// <summary>
    /// RSI state for the jukebox being on.
    /// </summary>
    [DataField]
    public string? OnState;

    /// <summary>
    /// RSI state for the jukebox being on.
    /// </summary>
    [DataField]
    public string? OffState;

    /// <summary>
    /// RSI state for the jukebox track being selected.
    /// </summary>
    [DataField]
    public string? SelectState;

    [ViewVariables]
    public bool Selecting;

    [ViewVariables]
    public float SelectAccumulator;

    // VG-Tweak start
    [ViewVariables, AutoNetworkedField]
    public float Volume = 50f;

    public float MinVolume = -30f;
    public float MaxVolume = 0f;
    public float MinSlider = 0f;
    public float MaxSlider = 100f;

    [ViewVariables, AutoNetworkedField]
    public JukeboxRepeatMode RepeatMode = JukeboxRepeatMode.NoRepeat;

    [ViewVariables, AutoNetworkedField]
    public bool ShuffleEnabled;

    [ViewVariables, AutoNetworkedField]
    public List<ProtoId<JukeboxPrototype>> Playlist = new();

    [ViewVariables]
    public List<ProtoId<JukeboxPrototype>> Queue = new();

    [ViewVariables, AutoNetworkedField]
    public int CurrentQueueIndex = -1;

    [ViewVariables]
    public bool AutoAdvance;
    // VG-Tweak end
}

[Serializable, NetSerializable]
public sealed class JukeboxPlayingMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class JukeboxPauseMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class JukeboxStopMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class JukeboxSelectedMessage(ProtoId<JukeboxPrototype> songId) : BoundUserInterfaceMessage
{
    public ProtoId<JukeboxPrototype> SongId { get; } = songId;
}

[Serializable, NetSerializable]
public sealed class JukeboxSetTimeMessage(float songTime) : BoundUserInterfaceMessage
{
    public float SongTime { get; } = songTime;
}

// VG-Tweak start
[Serializable, NetSerializable]
public sealed class JukeboxSetVolumeMessage(float volume) : BoundUserInterfaceMessage
{
    public float Volume { get; } = volume;
}

[Serializable, NetSerializable]
public sealed class JukeboxSetRepeatMessage(JukeboxRepeatMode mode) : BoundUserInterfaceMessage
{
    public JukeboxRepeatMode Mode { get; } = mode;
}

[Serializable, NetSerializable]
public sealed class JukeboxSetShuffleMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled { get; } = enabled;
}

[Serializable, NetSerializable]
public enum JukeboxRepeatMode : byte
{
    NoRepeat,
    RepeatOne,
    RepeatAll
}

[Serializable, NetSerializable]
public sealed class JukeboxNextTrackMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class JukeboxPrevTrackMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class JukeboxPlaySelectedMessage(ProtoId<JukeboxPrototype> songId) : BoundUserInterfaceMessage
{
    public ProtoId<JukeboxPrototype> SongId { get; } = songId;
}
// VG-Tweak end

[Serializable, NetSerializable]
public enum JukeboxVisuals : byte
{
    VisualState
}

[Serializable, NetSerializable]
public enum JukeboxVisualState : byte
{
    On,
    Off,
    Select,
}

public enum JukeboxVisualLayers : byte
{
    Base
}