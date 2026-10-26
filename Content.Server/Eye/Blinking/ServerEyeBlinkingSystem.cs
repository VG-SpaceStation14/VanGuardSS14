using Content.Shared.Body;
using Content.Shared.Chat;
using Content.Shared.Damage.Systems;
using Content.Shared.Eye.Blinking;
using Robust.Shared.Random;

namespace Content.Server.Eye.Blinking;

/// <inheritdoc/>
public sealed partial class ServerEyeBlinkingSystem : EyeBlinkingSystem
{
    // VG-Tweak Start: reflex blink random
    [Dependency] private IRobustRandom _random = default!;
    // VG-Tweak End

    // TODO: Move all this stuff into Shared once chat is predicted.
    [SubscribeLocalEvent]
    private void OnEmote(Entity<EyeBlinkingComponent> ent, ref EmoteEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TrySendEmoteMessage(ent, args.Emote.ID);
    }

    [SubscribeLocalEvent]
    private void OnEmote(Entity<EyeBlinkingComponent> ent, ref BodyRelayedEvent<EmoteEvent> args)
    {
        if (args.Args.Handled)
            return;

        var handled = TrySendEmoteMessage(ent, args.Args.Emote.ID);
        args.Args = args.Args with { Handled = handled };
    }

    private bool TrySendEmoteMessage(Entity<EyeBlinkingComponent> ent, string emoteId)
    {
        if (!ent.Comp.BlinkEmoteId.Contains(emoteId))
            return false;

        if (ent.Comp.Status != BlinkStatus.Normal)
            return false;

        var ev = new BlinkEyeEvent(GetNetEntity(ent.Owner));
        RaiseNetworkEvent(ev);
        return true;
    }

    // VG-Tweak Start: reflex blink on damage
    /// <summary>
    /// Direct damage subscription - fires for body-mounted <see cref="EyeBlinkingComponent"/>.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnDamage(Entity<EyeBlinkingComponent> ent, ref DamageChangedEvent args)
    {
        TryReflexBlink(ent, args);
    }

    /// <summary>
    /// Relayed damage subscription - fires for organ-mounted <see cref="EyeBlinkingComponent"/>.
    /// Requires <see cref="BodySystem"/> to relay <see cref="DamageChangedEvent"/>; if that relay isn't wired,
    /// reflex blinks only fire for body-mounted components.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnDamage(Entity<EyeBlinkingComponent> ent, ref BodyRelayedEvent<DamageChangedEvent> args)
    {
        TryReflexBlink(ent, args.Args);
    }

    private void TryReflexBlink(Entity<EyeBlinkingComponent> ent, DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null)
            return;

        if (ent.Comp.Status != BlinkStatus.Normal)
            return;

        var damage = (float)args.DamageDelta.GetTotal();
        if (damage < ent.Comp.ReflexBlinkDamageThreshold)
            return;

        if (!_random.Prob(ent.Comp.ReflexBlinkChance))
            return;

        var ev = new BlinkEyeEvent(GetNetEntity(ent.Owner), ent.Comp.ReflexBlinkDuration);
        RaiseNetworkEvent(ev);
    }
    // VG-Tweak End
}