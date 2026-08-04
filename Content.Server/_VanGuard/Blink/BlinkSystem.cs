using System.Linq;
using Content.Shared._VanGuard.Blink;
using Content.Shared.Bed.Sleep;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Stunnable;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Maths;

namespace Content.Server._VanGuard.Blink;

public sealed partial class BlinkSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DamageableSystem _damageable = default!;

    private const float DefaultMaxHp = 100f;
    private readonly Dictionary<EntityUid, bool> _wasSleeping = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BlinkComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<BlinkComponent, DamageChangedEvent>(OnDamage);
    }

    private void OnMobStateChanged(EntityUid uid, BlinkComponent blink, MobStateChangedEvent args)
    {
        ApplyMobState(uid, blink, args.NewMobState);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<BlinkComponent, MobStateComponent>();
        var curTime = _timing.CurTime;

        while (query.MoveNext(out var uid, out var blink, out var mobState))
        {
            if (mobState.CurrentState != MobState.Alive)
                continue;

            bool sleeping = HasComp<SleepingComponent>(uid);
            if (sleeping != _wasSleeping.GetValueOrDefault(uid))
            {
                OnSleepChanged(uid, blink, sleeping);
                _wasSleeping[uid] = sleeping;
            }

            if (sleeping)
                continue;

            float intensity = GetBlinkIntensity(uid, blink);

            if (blink.EyesClosed)
            {
                if (curTime >= blink.BlinkEndTime)
                {
                    SetEyesClosed(uid, blink, false);
                    ScheduleNextBlink(uid, blink, intensity);
                }
                continue;
            }

            if (curTime >= blink.NextBlinkTime)
            {
                float skipChance = blink.SkipBlinkChance * (1f - intensity);
                if (_random.Prob(skipChance))
                {
                    ScheduleNextBlink(uid, blink, intensity);
                    continue;
                }
                StartBlink(uid, blink, intensity);
            }
        }
    }

    private void OnDamage(EntityUid uid, BlinkComponent blink, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null) return;
        if (!TryComp<MobStateComponent>(uid, out var mobState) || mobState.CurrentState != MobState.Alive) return;
        if (HasComp<SleepingComponent>(uid)) return;

        float damage = (float) args.DamageDelta.GetTotal();
        if (damage < blink.ReflexBlinkDamageThreshold || !_random.Prob(blink.ReflexBlinkChance)) return;

        TriggerReflexBlink(uid, blink, damage);
    }

    private void TriggerReflexBlink(EntityUid uid, BlinkComponent blink, float damage)
    {
        if (blink.EyesClosed)
        {
            blink.BlinkEndTime += TimeSpan.FromSeconds(blink.ReflexBlinkDuration);
            return;
        }

        SetEyesClosed(uid, blink, true);
        float multiplier = Math.Clamp(damage / (blink.ReflexBlinkDamageThreshold * 2f), 1f, 2f);
        blink.BlinkEndTime = _timing.CurTime + TimeSpan.FromSeconds(blink.ReflexBlinkDuration * multiplier);
        blink.NextBlinkTime = blink.BlinkEndTime;
    }

    private void StartBlink(EntityUid uid, BlinkComponent blink, float intensity)
    {
        float duration = MathHelper.Lerp(blink.NormalBlinkDuration, blink.InjuredBlinkDuration, intensity);
        float longBlinkChance = blink.LongBlinkChance + intensity * blink.IntensityLongBlinkBonus;

        if (_random.Prob(longBlinkChance))
            duration *= blink.LongBlinkMultiplier;

        SetEyesClosed(uid, blink, true);
        blink.BlinkEndTime = _timing.CurTime + TimeSpan.FromSeconds(duration);
    }

    private void ScheduleNextBlink(EntityUid uid, BlinkComponent blink, float intensity)
    {
        float min = MathHelper.Lerp(blink.NormalMinBlinkInterval, blink.InjuredMinBlinkInterval, intensity);
        float max = MathHelper.Lerp(blink.NormalMaxBlinkInterval, blink.InjuredMaxBlinkInterval, intensity);

        if (_random.Prob(blink.LongIntervalChance * (1f - intensity)))
        {
            min *= blink.LongIntervalMultiplier;
            max *= blink.LongIntervalMultiplier;
        }

        blink.NextBlinkTime = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(min, max));
    }

    private void SetEyesClosed(EntityUid uid, BlinkComponent blink, bool closed)
    {
        if (blink.EyesClosed == closed) return;
        blink.EyesClosed = closed;
        Dirty(uid, blink);
    }

    private float GetBlinkIntensity(EntityUid uid, BlinkComponent blink)
    {
        float damage = GetDamageFraction(uid);
        float stamina = GetStaminaFraction(uid);
        return Math.Clamp(damage * blink.DamageWeight + stamina * blink.StaminaWeight, 0f, 1f);
    }

    private float GetDamageFraction(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable)) return 0f;
        var maxHP = FixedPoint2.New(DefaultMaxHp);
        if (TryComp<MobThresholdsComponent>(uid, out var thresholds) && thresholds.Thresholds.Count > 0)
            maxHP = thresholds.Thresholds.Keys.Max();

        var totalDamage = _damageable.GetTotalDamage((uid, damageable));
        return Math.Clamp((float)(totalDamage / maxHP), 0f, 1f);
    }

    private float GetStaminaFraction(EntityUid uid)
    {
        if (!TryComp<StaminaComponent>(uid, out var stamina) || stamina.CritThreshold <= 0f) return 0f;
        return Math.Clamp(stamina.StaminaDamage / stamina.CritThreshold, 0f, 1f);
    }

    private void ApplyMobState(EntityUid uid, BlinkComponent blink, MobState state)
    {
        switch (state)
        {
            case MobState.Alive:
                SetEyesClosed(uid, blink, false);
                ScheduleNextBlink(uid, blink, 0f);
                break;
            case MobState.Critical:
                SetEyesClosed(uid, blink, true);
                blink.NextBlinkTime = TimeSpan.MaxValue;
                blink.BlinkEndTime = TimeSpan.MaxValue;
                break;
            case MobState.Dead:
                SetEyesClosed(uid, blink, false);
                blink.NextBlinkTime = TimeSpan.MaxValue;
                blink.BlinkEndTime = TimeSpan.MaxValue;
                break;
        }
    }

    private void OnSleepChanged(EntityUid uid, BlinkComponent blink, bool sleeping)
    {
        if (sleeping)
        {
            SetEyesClosed(uid, blink, true);
            blink.NextBlinkTime = TimeSpan.MaxValue;
            blink.BlinkEndTime = TimeSpan.MaxValue;
        }
        else
        {
            if (TryComp<MobStateComponent>(uid, out var mobState))
                ApplyMobState(uid, blink, mobState.CurrentState);
        }
    }
}