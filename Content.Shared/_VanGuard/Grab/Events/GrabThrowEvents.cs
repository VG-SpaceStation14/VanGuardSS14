using Robust.Shared.Map;

namespace Content.Shared._VanGuard.Grab;

/// <summary>
/// Raised when the player tries to throw the virtual item representing a grabbed target.
/// The grab system can consume this and throw the real target instead.
/// </summary>
public sealed class GrabThrowRequestEvent(EntityUid user, EntityUid target, EntityCoordinates coordinates)
    : HandledEntityEventArgs
{
    public readonly EntityUid User = user;
    public readonly EntityUid Target = target;
    public readonly EntityCoordinates Coordinates = coordinates;
}
