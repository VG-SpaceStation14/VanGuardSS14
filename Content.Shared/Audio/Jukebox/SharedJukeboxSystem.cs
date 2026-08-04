using Robust.Shared.Audio.Systems;

namespace Content.Shared.Audio.Jukebox;

public abstract partial class SharedJukeboxSystem : EntitySystem
{
    [Dependency] protected SharedAudioSystem Audio = default!;

    /// <summary>
    /// Returns whether or not the given jukebox is currently playing a song.
    /// </summary>
    public bool IsPlaying(Entity<JukeboxComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp))
            return false;

        return entity.Comp.AudioStream is { } audio && Audio.IsPlaying(audio);
    }

    // VG-Tweak start
    /// <summary>
    /// Maps a value from one range to another.
    /// </summary>
    public static float MapToRange(float value, float fromMin, float fromMax, float toMin, float toMax)
    {
        var fromRange = fromMax - fromMin;
        var toRange = toMax - toMin;
        if (fromRange == 0)
            return toMin;
        return (value - fromMin) / fromRange * toRange + toMin;
    }
    // VG-Tweak end
}