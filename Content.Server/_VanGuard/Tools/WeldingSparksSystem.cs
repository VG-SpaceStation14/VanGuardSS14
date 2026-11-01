using Content.Shared._VanGuard.Tools;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._VanGuard.Tools;

/// <summary>
/// Runs the looping welding sound and spark entity for each active welding do-after.
/// </summary>
public sealed partial class WeldingSparksSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;

    private sealed class ActiveWeld
    {
        public EntityUid Tool;
        public EntityUid Spark;
    }

    private readonly Dictionary<DoAfterId, ActiveWeld> _running = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<DoAfterComponent>();
        while (query.MoveNext(out var uid, out var doAfters))
        {
            foreach (var (index, doAfter) in doAfters.DoAfters)
            {
                var id = new DoAfterId(uid, index);

                if (doAfter.Completed || doAfter.Cancelled)
                {
                    Finish(id);
                    continue;
                }

                if (!_running.ContainsKey(id) && TryStart(id, doAfter))
                    continue;
            }
        }

        RemoveStaleWelds();
    }

    private bool TryStart(DoAfterId id, Content.Shared.DoAfter.DoAfter doAfter)
    {
        if (doAfter.Args.Used is not { } tool || doAfter.Args.Target is not { } target)
            return false;

        if (!TryComp<WeldingSparksComponent>(tool, out var sparks))
            return false;

        var effect = Spawn(sparks.EffectPrototype, Transform(target).Coordinates);
        BeginSound(tool);

        _running[id] = new ActiveWeld
        {
            Tool = tool,
            Spark = effect,
        };

        RaiseNetworkEvent(new SpawnedWeldingSparksEvent(
            GetNetEntity(target),
            GetNetEntity(effect),
            doAfter.Args.Delay));

        return true;
    }

    private void Finish(DoAfterId id)
    {
        if (!_running.Remove(id, out var weld))
            return;

        EndSound(weld.Tool);

        if (Exists(weld.Spark))
            QueueDel(weld.Spark);
    }

    private void RemoveStaleWelds()
    {
        if (_running.Count == 0)
            return;

        List<DoAfterId>? stale = null;
        foreach (var (id, weld) in _running)
        {
            if (Exists(weld.Tool) && Exists(weld.Spark))
                continue;

            stale ??= new List<DoAfterId>();
            stale.Add(id);
        }

        if (stale == null)
            return;

        foreach (var id in stale)
        {
            if (_running.Remove(id, out var weld))
            {
                EndSound(weld.Tool);
                if (Exists(weld.Spark))
                    QueueDel(weld.Spark);
            }
        }
    }

    private void BeginSound(EntityUid tool)
    {
        if (!TryComp<WeldingSoundComponent>(tool, out var sound))
            return;

        if (sound.ActiveStream is { } existing && Exists(existing))
            return;

        var played = _audio.PlayPvs(
            sound.Sound,
            tool,
            AudioParams.Default.WithVolume(sound.Volume).WithLoop(true));

        if (played == null)
            return;

        sound.ActiveStream = played.Value.Entity;
        Dirty(tool, sound);
    }

    private void EndSound(EntityUid tool)
    {
        if (!TryComp<WeldingSoundComponent>(tool, out var sound))
            return;

        if (sound.ActiveStream is { } stream)
            _audio.Stop(stream);

        sound.ActiveStream = null;
        Dirty(tool, sound);
    }
}
