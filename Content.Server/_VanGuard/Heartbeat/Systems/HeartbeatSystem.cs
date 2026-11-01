using Content.Server._VanGuard.Heartbeat.Components;
using Content.Shared._VanGuard.Heartbeat;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Heartbeat.Systems;

/// <summary>
/// Plays a looping heartbeat while a mob is in critical condition. The beat interval and pitch are
/// derived from the amount of damage the mob has accumulated.
/// </summary>
public sealed partial class HeartbeatSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private const float FastestInterval = 0.5f;
    private const float SlowestInterval = 3f;

    private static readonly SoundSpecifier BeatSound = new SoundPathSpecifier(
        "/Audio/_VanGuard/Effects/heartbeat.ogg",
        AudioParams.Default.WithVolume(-3f));

    private readonly HashSet<ICommonSession> _mutedSessions = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CritHeartbeatComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<CritHeartbeatComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeNetworkEvent<HeartbeatOptionsChangedEvent>(OnPreferencesChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CritHeartbeatComponent>();
        while (query.MoveNext(out var uid, out var heartbeat))
        {
            if (!_mobState.IsCritical(uid))
                continue;

            if (_timing.CurTime < heartbeat.NextBeat)
                continue;

            if (_players.TryGetSessionByEntity(uid, out var session) && _mutedSessions.Contains(session))
                continue;

            _audio.PlayGlobal(BeatSound, uid, AudioParams.Default.WithPitchScale(heartbeat.Pitch));
            heartbeat.NextBeat = _timing.CurTime + heartbeat.Interval;
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _mutedSessions.Clear();
    }

    private void OnPreferencesChanged(HeartbeatOptionsChangedEvent args, EntitySessionEventArgs session)
    {
        if (args.IsEnabled)
            _mutedSessions.Remove(session.SenderSession);
        else
            _mutedSessions.Add(session.SenderSession);
    }

    private void OnMobStateChanged(Entity<CritHeartbeatComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Critical)
        {
            ent.Comp.NextBeat = TimeSpan.Zero;
            return;
        }

        RecalculateBeat(ent);
    }

    private void OnDamageChanged(Entity<CritHeartbeatComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || !_mobState.IsCritical(ent.Owner))
            return;

        RecalculateBeat(ent);
    }

    private void RecalculateBeat(Entity<CritHeartbeatComponent> ent)
    {
        if (!TryComp<DamageableComponent>(ent, out var damageable))
            return;

        var total = (float) _damage.GetTotalDamage((ent.Owner, damageable));
        if (total <= 0f)
            return;

        ent.Comp.Pitch = MathF.Min(1f, 100f / total);

        var overkill = MathF.Max(0f, total - 100f);
        var seconds = FastestInterval + overkill / 100f * (SlowestInterval - FastestInterval);
        ent.Comp.Interval = TimeSpan.FromSeconds(seconds);
    }
}
