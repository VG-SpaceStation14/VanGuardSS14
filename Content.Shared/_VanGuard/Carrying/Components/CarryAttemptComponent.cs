using Content.Shared.DoAfter;
using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Carrying;

/// <summary>
/// Server-side guard against multiple simultaneous pickup attempts by one carrier.
/// </summary>
[RegisterComponent]
public sealed partial class CarryAttemptComponent : Component
{
    public EntityUid Target;
    public DoAfterId? DoAfter;
}
