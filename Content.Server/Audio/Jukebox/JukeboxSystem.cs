using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Audio.Jukebox;
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;
using JukeboxComponent = Content.Shared.Audio.Jukebox.JukeboxComponent;

namespace Content.Server.Audio.Jukebox;

public sealed partial class JukeboxSystem : SharedJukeboxSystem
{
    [Dependency] private AppearanceSystem _appearanceSystem = default!;
    // VG-Tweak start
    [Dependency] private IRobustRandom _random = default!;
    // VG-Tweak end

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JukeboxComponent, JukeboxSelectedMessage>(OnJukeboxSelected);
        SubscribeLocalEvent<JukeboxComponent, JukeboxPlayingMessage>(OnJukeboxPlay);
        SubscribeLocalEvent<JukeboxComponent, JukeboxPauseMessage>(OnJukeboxPause);
        SubscribeLocalEvent<JukeboxComponent, JukeboxStopMessage>(OnJukeboxStop);
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetTimeMessage>(OnJukeboxSetTime);
        SubscribeLocalEvent<JukeboxComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<JukeboxComponent, ComponentShutdown>(OnComponentShutdown);
        // VG-Tweak start
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetVolumeMessage>(OnJukeboxSetVolume);
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetRepeatMessage>(OnJukeboxSetRepeat);
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetShuffleMessage>(OnJukeboxSetShuffle);
        SubscribeLocalEvent<JukeboxComponent, JukeboxNextTrackMessage>(OnJukeboxNextTrack);
        SubscribeLocalEvent<JukeboxComponent, JukeboxPrevTrackMessage>(OnJukeboxPrevTrack);
        SubscribeLocalEvent<JukeboxComponent, JukeboxPlaySelectedMessage>(OnJukeboxPlaySelected);
        // VG-Tweak end

        SubscribeLocalEvent<JukeboxComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnComponentInit(Entity<JukeboxComponent> ent, ref ComponentInit args)
    {
        // VG-Tweak start
        RefreshPlaylist(ent, ent.Comp);
        // VG-Tweak end

        if (HasComp<ApcPowerReceiverComponent>(ent))
        {
            TryUpdateVisualState(ent.AsNullable());
        }
    }

    // VG-Tweak start
    private void RefreshPlaylist(Entity<JukeboxComponent> ent, JukeboxComponent component)
    {
        component.Playlist.Clear();
        foreach (var proto in ProtoMan.EnumeratePrototypes<JukeboxPrototype>())
        {
            component.Playlist.Add(proto.ID);
        }
    }
    // VG-Tweak end

    private void OnJukeboxPlay(Entity<JukeboxComponent> ent, ref JukeboxPlayingMessage args)
    {
        TryPlay(ent.AsNullable());
    }

    private void OnJukeboxPause(Entity<JukeboxComponent> ent, ref JukeboxPauseMessage args)
    {
        Pause(ent.AsNullable());
        // VG-Tweak start
        ent.Comp.AutoAdvance = false;
        // VG-Tweak end
    }

    private void OnJukeboxSetTime(Entity<JukeboxComponent> ent, ref JukeboxSetTimeMessage args)
    {
        if (TryComp(args.Actor, out ActorComponent? actorComp))
        {
            var offset = actorComp.PlayerSession.Channel.Ping * 1.5f / 1000f;
            SetTime(ent.AsNullable(), args.SongTime + offset);
        }
    }

    // VG-Tweak start
    private void OnJukeboxSetVolume(Entity<JukeboxComponent> ent, ref JukeboxSetVolumeMessage args)
    {
        SetJukeboxVolume(ent, ent.Comp, args.Volume);

        if (!TryComp<AudioComponent>(ent.Comp.AudioStream, out var audioComponent))
            return;

        Audio.SetVolume(ent.Comp.AudioStream, MapToRange(args.Volume, ent.Comp.MinSlider, ent.Comp.MaxSlider, ent.Comp.MinVolume, ent.Comp.MaxVolume));
    }

    private void SetJukeboxVolume(Entity<JukeboxComponent> ent, JukeboxComponent component, float volume)
    {
        component.Volume = volume;
        Dirty(ent);
    }
    // VG-Tweak end

    private void OnPowerChanged(Entity<JukeboxComponent> entity, ref PowerChangedEvent args)
    {
        TryUpdateVisualState(entity.AsNullable());

        if (!this.IsPowered(entity.Owner, EntityManager))
        {
            Stop(entity.AsNullable());
        }
    }

    private void OnJukeboxStop(Entity<JukeboxComponent> entity, ref JukeboxStopMessage args)
    {
        Stop(entity.AsNullable());
    }

    private void OnJukeboxSelected(EntityUid uid, JukeboxComponent component, JukeboxSelectedMessage args)
    {
        SetSelectedTrack((uid, component), args.SongId);
    }

    // VG-Tweak start
    private void OnJukeboxNextTrack(Entity<JukeboxComponent> ent, ref JukeboxNextTrackMessage args)
    {
        if (!ent.Comp.AutoAdvance || ent.Comp.AudioStream == null)
            return;

        var nextTrack = GetNextTrack(ent, ent.Comp);
        if (nextTrack != null && ProtoMan.Resolve(nextTrack.Value, out var nextProto))
        {
            ent.Comp.SelectedSongId = nextTrack.Value;
            ent.Comp.AudioStream = Audio.Stop(ent.Comp.AudioStream);
            ent.Comp.AudioStream = Audio.PlayPvs(nextProto.Path, ent, AudioParams.Default.WithMaxDistance(10f).WithVolume(MapToRange(ent.Comp.Volume, ent.Comp.MinSlider, ent.Comp.MaxSlider, ent.Comp.MinVolume, ent.Comp.MaxVolume)))?.Entity;
            ent.Comp.AutoAdvance = true;
            Dirty(ent);
        }
    }

    private void OnJukeboxPrevTrack(Entity<JukeboxComponent> ent, ref JukeboxPrevTrackMessage args)
    {
        if (!ent.Comp.AutoAdvance || ent.Comp.AudioStream == null)
            return;

        if (ent.Comp.Queue.Count == 0 || ent.Comp.CurrentQueueIndex < 0)
            return;

        int prevIndex;
        if (ent.Comp.RepeatMode == JukeboxRepeatMode.RepeatAll)
        {
            prevIndex = ent.Comp.CurrentQueueIndex - 1;
            if (prevIndex < 0)
                prevIndex = ent.Comp.Queue.Count - 1;
        }
        else
        {
            prevIndex = ent.Comp.CurrentQueueIndex - 1;
            if (prevIndex < 0)
                return;
        }

        var prevTrack = ent.Comp.Queue[prevIndex];
        if (ProtoMan.Resolve(prevTrack, out var prevProto))
        {
            ent.Comp.SelectedSongId = prevTrack;
            ent.Comp.CurrentQueueIndex = prevIndex;
            ent.Comp.AudioStream = Audio.Stop(ent.Comp.AudioStream);
            ent.Comp.AudioStream = Audio.PlayPvs(prevProto.Path, ent, AudioParams.Default.WithMaxDistance(10f).WithVolume(MapToRange(ent.Comp.Volume, ent.Comp.MinSlider, ent.Comp.MaxSlider, ent.Comp.MinVolume, ent.Comp.MaxVolume)))?.Entity;
            ent.Comp.AutoAdvance = true;
            Dirty(ent);
        }
    }

    private void OnJukeboxPlaySelected(Entity<JukeboxComponent> ent, ref JukeboxPlaySelectedMessage args)
    {
        ent.Comp.SelectedSongId = args.SongId;
        ent.Comp.AudioStream = Audio.Stop(ent.Comp.AudioStream);

        if (ProtoMan.Resolve(args.SongId, out var jukeboxProto))
        {
            ent.Comp.AudioStream = Audio.PlayPvs(jukeboxProto.Path, ent, AudioParams.Default.WithMaxDistance(10f).WithVolume(MapToRange(ent.Comp.Volume, ent.Comp.MinSlider, ent.Comp.MaxSlider, ent.Comp.MinVolume, ent.Comp.MaxVolume)))?.Entity;

            ent.Comp.Queue.Clear();
            ent.Comp.Queue.AddRange(ent.Comp.ShuffleEnabled
                ? ent.Comp.Playlist.OrderBy(_ => _random.Next()).ToList()
                : ent.Comp.Playlist.ToList());

            var index = ent.Comp.Queue.IndexOf(args.SongId);
            ent.Comp.CurrentQueueIndex = index >= 0 ? index : 0;
            ent.Comp.AutoAdvance = true;

            DirectSetVisualState(ent, JukeboxVisualState.Select);
            ent.Comp.Selecting = true;
            ent.Comp.SelectAccumulator = 0f;

            Dirty(ent);
        }
    }
    // VG-Tweak end

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<JukeboxComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Selecting)
            {
                comp.SelectAccumulator += frameTime;
                if (comp.SelectAccumulator >= 0.5f)
                {
                    comp.SelectAccumulator = 0f;
                    comp.Selecting = false;

                    TryUpdateVisualState((uid, comp));
                }
            }

            // VG-Tweak start
            if (comp.AutoAdvance && comp.AudioStream != null)
            {
                if (!TryComp<AudioComponent>(comp.AudioStream, out var audio))
                {
                    TryAdvanceToNextTrack((uid, comp));
                    continue;
                }

                if (ProtoMan.TryIndex(comp.SelectedSongId, out var trackProto))
                {
                    var length = (float)Audio.GetAudioLength(trackProto.Path.Path.ToString()).TotalSeconds;

                    if (audio.PlaybackPosition >= length - 0.2f)
                    {
                        TryAdvanceToNextTrack((uid, comp));
                    }
                }
            }
            // VG-Tweak end
        }
    }

    // VG-Tweak start
    private void TryAdvanceToNextTrack(Entity<JukeboxComponent> ent)
    {
        var nextTrack = GetNextTrack(ent, ent.Comp);
        if (nextTrack != null && ProtoMan.Resolve(nextTrack.Value, out var nextProto))
        {
            ent.Comp.SelectedSongId = nextTrack.Value;
            ent.Comp.AudioStream = Audio.Stop(ent.Comp.AudioStream);
            ent.Comp.AudioStream = Audio.PlayPvs(nextProto.Path, ent, AudioParams.Default.WithMaxDistance(10f).WithVolume(MapToRange(ent.Comp.Volume, ent.Comp.MinSlider, ent.Comp.MaxSlider, ent.Comp.MinVolume, ent.Comp.MaxVolume)))?.Entity;
            ent.Comp.AutoAdvance = true;
            Dirty(ent);
        }
        else
        {
            ent.Comp.AutoAdvance = false;
            ent.Comp.AudioStream = Audio.Stop(ent.Comp.AudioStream);
            ent.Comp.SelectedSongId = null;
            ent.Comp.CurrentQueueIndex = -1;
            Dirty(ent);
        }
    }

    private ProtoId<JukeboxPrototype>? GetNextTrack(Entity<JukeboxComponent> ent, JukeboxComponent component)
    {
        if (component.Playlist.Count == 0)
            return null;

        if (component.Queue.Count == 0)
        {
            component.Queue.Clear();
            component.Queue.AddRange(component.ShuffleEnabled
                ? component.Playlist.OrderBy(_ => _random.Next()).ToList()
                : component.Playlist.ToList());
            component.CurrentQueueIndex = -1;
        }

        switch (component.RepeatMode)
        {
            case JukeboxRepeatMode.RepeatOne:
                return component.SelectedSongId;

            case JukeboxRepeatMode.RepeatAll:
                component.CurrentQueueIndex++;
                if (component.CurrentQueueIndex >= component.Queue.Count)
                    component.CurrentQueueIndex = 0;
                return component.Queue[component.CurrentQueueIndex];

            case JukeboxRepeatMode.NoRepeat:
            default:
                component.CurrentQueueIndex++;
                if (component.CurrentQueueIndex >= component.Queue.Count)
                {
                    component.CurrentQueueIndex = -1;
                    return null;
                }
                return component.Queue[component.CurrentQueueIndex];
        }
    }

    private void OnJukeboxSetRepeat(Entity<JukeboxComponent> ent, ref JukeboxSetRepeatMessage args)
    {
        ent.Comp.RepeatMode = args.Mode;
        Dirty(ent);
    }

    private void OnJukeboxSetShuffle(Entity<JukeboxComponent> ent, ref JukeboxSetShuffleMessage args)
    {
        if (ent.Comp.ShuffleEnabled == args.Enabled)
            return;

        ent.Comp.ShuffleEnabled = args.Enabled;

        ent.Comp.Queue.Clear();
        ent.Comp.Queue.AddRange(ent.Comp.ShuffleEnabled
            ? ent.Comp.Playlist.OrderBy(_ => _random.Next()).ToList()
            : ent.Comp.Playlist.ToList());

        if (ent.Comp.SelectedSongId != null)
        {
            var index = ent.Comp.Queue.IndexOf(ent.Comp.SelectedSongId.Value);
            ent.Comp.CurrentQueueIndex = index >= 0 ? index : 0;
        }
        else
        {
            ent.Comp.CurrentQueueIndex = -1;
        }

        Dirty(ent);
    }
    // VG-Tweak end

    private void OnComponentShutdown(Entity<JukeboxComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.AudioStream = Audio.Stop(ent.Comp.AudioStream);
    }

    private void DirectSetVisualState(EntityUid uid, JukeboxVisualState state)
    {
        _appearanceSystem.SetData(uid, JukeboxVisuals.VisualState, state);
    }

    private void TryUpdateVisualState(Entity<JukeboxComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        var finalState = JukeboxVisualState.On;

        if (!this.IsPowered(ent, EntityManager))
        {
            finalState = JukeboxVisualState.Off;
        }

        _appearanceSystem.SetData(ent, JukeboxVisuals.VisualState, finalState);
    }

    /// <summary>
    /// Set the selected track of the jukebox to the specified prototype.
    /// </summary>
    public void SetSelectedTrack(Entity<JukeboxComponent?> ent, ProtoId<JukeboxPrototype> track)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        if (!Audio.IsPlaying(ent.Comp.AudioStream))
        {
            ent.Comp.SelectedSongId = track;
            DirectSetVisualState(ent, JukeboxVisualState.Select);
            ent.Comp.Selecting = true;
            ent.Comp.AudioStream = Audio.Stop(ent.Comp.AudioStream);
            // VG-Tweak start
            ent.Comp.Queue.Clear();
            ent.Comp.Queue.AddRange(ent.Comp.ShuffleEnabled
                ? ent.Comp.Playlist.OrderBy(_ => _random.Next()).ToList()
                : ent.Comp.Playlist.ToList());

            var index = ent.Comp.Queue.IndexOf(track);
            ent.Comp.CurrentQueueIndex = index >= 0 ? index : 0;
            ent.Comp.AutoAdvance = false;
            // VG-Tweak end
            Dirty(ent);
        }
    }

    /// <summary>
    /// Attempts to play the jukebox's current selected track.
    /// </summary>
    /// <returns>false if no track is selected or the track prototype cannot be found, otherwise true.</returns>
    public bool TryPlay(Entity<JukeboxComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return false;

        if (Exists(ent.Comp.AudioStream))
        {
            Audio.SetState(ent.Comp.AudioStream, AudioState.Playing);
            // VG-Tweak start
            ent.Comp.AutoAdvance = true;
            // VG-Tweak end
        }
        else
        {
            if (string.IsNullOrEmpty(ent.Comp.SelectedSongId) ||
                !ProtoMan.Resolve(ent.Comp.SelectedSongId, out var jukeboxProto))
            {
                // VG-Tweak start
                var nonNullableEnt = new Entity<JukeboxComponent>(ent.Owner, ent.Comp);
                var nextTrack = GetNextTrack(nonNullableEnt, ent.Comp);
                if (nextTrack == null || !ProtoMan.Resolve(nextTrack.Value, out jukeboxProto))
                    return false;

                ent.Comp.SelectedSongId = nextTrack.Value;
                ent.Comp.AutoAdvance = true;
                // VG-Tweak end
            }

            ent.Comp.AudioStream = Audio.PlayPvs(jukeboxProto.Path, ent, AudioParams.Default.WithMaxDistance(10f).WithVolume(MapToRange(ent.Comp.Volume, ent.Comp.MinSlider, ent.Comp.MaxSlider, ent.Comp.MinVolume, ent.Comp.MaxVolume)))?.Entity;
            Dirty(ent);
        }
        return true;
    }

    /// <summary>
    /// Stops any track that may currently be playing.
    /// </summary>
    public void Stop(Entity<JukeboxComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, logMissing: false))
            return;

        Audio.SetState(entity.Comp.AudioStream, AudioState.Stopped);
        // VG-Tweak start
        entity.Comp.AutoAdvance = false;
        // VG-Tweak end
    }

    /// <summary>
    /// Pauses any track that may currently be playing.
    /// </summary>
    public void Pause(Entity<JukeboxComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, logMissing: false))
            return;

        Audio.SetState(entity.Comp.AudioStream, AudioState.Paused);
    }

    /// <summary>
    /// Sets the playback position within the current audio track.
    /// </summary>
    /// <remarks>
    /// If setting based on user input, you may need to compensate for the player's ping.
    /// </remarks>
    public void SetTime(Entity<JukeboxComponent?> entity, float songTime)
    {
        if (!Resolve(entity, ref entity.Comp))
            return;

        Audio.SetPlaybackPosition(entity.Comp.AudioStream, songTime);
    }
}
