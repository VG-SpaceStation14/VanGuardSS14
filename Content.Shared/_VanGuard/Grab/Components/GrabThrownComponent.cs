namespace Content.Shared._VanGuard.Grab;

/// <summary>
/// Marks a mob currently thrown by a hard/choke grab so it falls when it lands.
/// </summary>
[RegisterComponent]
public sealed partial class GrabThrownComponent : Component
{
    public EntityUid? Thrower;
}
