using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Carrying;

/// <summary>
/// Server-side marker on a pickup target. It lets the target's termination
/// cancel the active do-after before SharedDoAfterSystem updates a deleted target.
/// </summary>
[RegisterComponent]
public sealed partial class CarryAttemptTargetComponent : Component
{
    public EntityUid Carrier;
}
