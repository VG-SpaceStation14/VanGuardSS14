using System.Collections.Generic;
using Content.Shared._VanGuard.Tools;
using Content.Shared.DoAfter;
using Content.Shared.Tools.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Audio;

namespace Content.Server._VanGuard.Tools;

public sealed partial class WeldingSparksSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private readonly Dictionary<(EntityUid user, ushort index), WeldingSparksData> _activeWelding = new();

    private struct WeldingSparksData
    {
        public EntityUid EffectEntity;
        public EntityUid ToolEntity;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<DoAfterComponent>();
        while (query.MoveNext(out var uid, out var doAfterComp))
        {
            foreach (var (idx, doAfter) in doAfterComp.DoAfters)
            {
                var key = (uid, idx);

                if (doAfter.Completed || doAfter.Cancelled)
                {
                    if (_activeWelding.TryGetValue(key, out var data))
                    {
                        StopWeldingSound(data.ToolEntity);
                        
                        if (Exists(data.EffectEntity))
                            QueueDel(data.EffectEntity);
                        
                        _activeWelding.Remove(key);
                    }
                    continue;
                }

                if (!_activeWelding.ContainsKey(key))
                {
                    if (doAfter.Args.Used != null && 
                        TryComp<WeldingSparksComponent>(doAfter.Args.Used, out var sparks) &&
                        doAfter.Args.Target is { } target)
                    {
                        var effect = Spawn(sparks.EffectPrototype, Transform(target).Coordinates);
                        
                        StartWeldingSound(doAfter.Args.Used.Value);
                        
                        _activeWelding[key] = new WeldingSparksData
                        {
                            EffectEntity = effect,
                            ToolEntity = doAfter.Args.Used.Value
                        };

                        RaiseNetworkEvent(new SpawnedWeldingSparksEvent(
                            GetNetEntity(target),
                            GetNetEntity(effect),
                            doAfter.Args.Delay
                        ));
                    }
                    else
                    {
                        _activeWelding[key] = new WeldingSparksData
                        {
                            EffectEntity = EntityUid.Invalid,
                            ToolEntity = EntityUid.Invalid
                        };
                    }
                }
            }
        }

        CleanupInvalidEntries();
    }

    private void StartWeldingSound(EntityUid tool)
    {
        if (!TryComp<WeldingSoundComponent>(tool, out var soundComp))
            return;

        if (soundComp.StreamHandle != null && Exists(soundComp.StreamHandle))
            return;

        var audioParams = AudioParams.Default.WithVolume(soundComp.Volume).WithLoop(true);
        var stream = _audio.PlayPvs(soundComp.Sound, tool, audioParams);
        
        if (stream != null)
        {
            soundComp.StreamHandle = stream.Value.Entity;
            Dirty(tool, soundComp);
        }
    }

    private void StopWeldingSound(EntityUid tool)
    {
        if (!TryComp<WeldingSoundComponent>(tool, out var soundComp))
            return;

        if (soundComp.StreamHandle != null)
        {
            _audio.Stop(soundComp.StreamHandle.Value);
            soundComp.StreamHandle = null;
            Dirty(tool, soundComp);
        }
    }

    private void CleanupInvalidEntries()
    {
        var toRemove = new List<(EntityUid, ushort)>();
        
        foreach (var kvp in _activeWelding)
        {
            if (kvp.Value.EffectEntity == EntityUid.Invalid && 
                kvp.Value.ToolEntity == EntityUid.Invalid)
            {
                toRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in toRemove)
        {
            _activeWelding.Remove(key);
        }
    }
}