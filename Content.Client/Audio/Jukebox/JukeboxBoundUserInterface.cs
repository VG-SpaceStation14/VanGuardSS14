using Content.Shared.Audio.Jukebox;
using Robust.Client.Audio;
using Robust.Client.UserInterface;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;

namespace Content.Client.Audio.Jukebox;

public sealed partial class JukeboxBoundUserInterface : BoundUserInterface
{
    [Dependency] private IPrototypeManager _protoManager = default!;

    [ViewVariables]
    private JukeboxMenu? _menu;

    public JukeboxBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<JukeboxMenu>();

        _menu.OnPlayPressed += args =>
        {
            if (args)
            {
                SendMessage(new JukeboxPlayingMessage());
            }
            else
            {
                SendMessage(new JukeboxPauseMessage());
            }
        };

        _menu.OnStopPressed += () =>
        {
            SendMessage(new JukeboxStopMessage());
        };

        _menu.OnSongSelected += SelectSong;
        // VG-Tweak start
        _menu.OnPlaySelected += PlaySelectedSong;
        _menu.SetVolume += SetVolume;
        _menu.OnRepeatModeChanged += mode =>
        {
            SendMessage(new JukeboxSetRepeatMessage(mode));
        };
        _menu.OnShuffleToggled += enabled =>
        {
            SendMessage(new JukeboxSetShuffleMessage(enabled));
        };
        _menu.OnNextTrack += () =>
        {
            SendMessage(new JukeboxNextTrackMessage());
        };
        _menu.OnPrevTrack += () =>
        {
            SendMessage(new JukeboxPrevTrackMessage());
        };
        // VG-Tweak end

        _menu.SetTime += SetTime;
        // VG-Tweak start
        PopulateMusic();
        Reload();
        // VG-Tweak end
    }

    /// <summary>
    /// Reloads the attached menu if it exists.
    /// </summary>
    public void Reload()
    {
        if (_menu == null || !EntMan.TryGetComponent(Owner, out JukeboxComponent? jukebox))
            return;

        _menu.SetAudioStream(jukebox.AudioStream);
        // VG-Tweak start
        _menu.SetVolumeSlider(jukebox.Volume);
        _menu.SetRepeatMode(jukebox.RepeatMode);
        _menu.SetShuffleEnabled(jukebox.ShuffleEnabled);
        // VG-Tweak end

        if (_protoManager.Resolve(jukebox.SelectedSongId, out var songProto))
        {
            var length = EntMan.System<AudioSystem>().GetAudioLength(songProto.Path.Path.ToString());
            _menu.SetSelectedSong(songProto.Name, (float) length.TotalSeconds);
        }
        else
        {
            _menu.SetSelectedSong(string.Empty, 0f);
        }
    }

    public void PopulateMusic()
    {
        _menu?.Populate(_protoManager.EnumeratePrototypes<JukeboxPrototype>());
    }

    public void SelectSong(ProtoId<JukeboxPrototype> songid)
    {
        SendMessage(new JukeboxSelectedMessage(songid));
    }

    // VG-Tweak start
    private void PlaySelectedSong(ProtoId<JukeboxPrototype> songid)
    {
        SendMessage(new JukeboxPlaySelectedMessage(songid));
    }

    public void SetVolume(float volume)
    {
        var sentVolume = volume;

        if (EntMan.TryGetComponent(Owner, out JukeboxComponent? jukebox) &&
            EntMan.TryGetComponent(jukebox.AudioStream, out AudioComponent? audioComp))
        {
            audioComp.Volume = SharedJukeboxSystem.MapToRange(volume, jukebox.MinSlider, jukebox.MaxSlider, jukebox.MinVolume, jukebox.MaxVolume);
        }

        SendMessage(new JukeboxSetVolumeMessage(sentVolume));
    }
    // VG-Tweak end

    public void SetTime(float time)
    {
        var sentTime = time;

        // You may be wondering, what the fuck is this
        // Well we want to be able to predict the playback slider change, of which there are many ways to do it
        // We can't just use SendPredictedMessage because it will reset every tick and audio updates every frame
        // so it will go BRRRRT
        // Using ping gets us close enough that it SHOULD, MOST OF THE TIME, fall within the 0.1 second tolerance
        // that's still on engine so our playback position never gets corrected.
        if (EntMan.TryGetComponent(Owner, out JukeboxComponent? jukebox) &&
            EntMan.TryGetComponent(jukebox.AudioStream, out AudioComponent? audioComp))
        {
            audioComp.PlaybackPosition = time;
        }

        SendMessage(new JukeboxSetTimeMessage(sentTime));
    }
}