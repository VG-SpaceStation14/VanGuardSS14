using Content.Shared.Eye.Blinking;

namespace Content.Client.Eye.Blinking;

public sealed partial class ClientEyeBlinkingSystem
{
    /// <summary>
    ///     Dead entities keep their eyes open and must not start new blink cycles.
    /// </summary>
    private static bool ShouldCloseEyes(BlinkStatus status)
    {
        return status != BlinkStatus.Normal && !status.HasFlag(BlinkStatus.Dead);
    }

    /// <summary>
    ///     Cancels an in-progress blink and clears its scheduled eyelid timings.
    /// </summary>
    private void CancelBlink(Entity<EyeBlinkingComponent> ent)
    {
        ent.Comp.BlinkInProgress = false;
        ent.Comp.NextOpenEyesTime = _timing.CurTime;

        foreach (var eyelid in ent.Comp.Eyelids)
        {
            eyelid.IsCompleteBlink = false;
            eyelid.ScheduledCloseTime = TimeSpan.Zero;
            eyelid.ScheduledOpenTime = TimeSpan.Zero;
        }
    }
}
