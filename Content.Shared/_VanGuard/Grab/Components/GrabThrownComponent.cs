namespace Content.Shared._VanGuard.Grab;

/// <summary>
/// Marks a mob currently thrown by a hard/choke grab so collisions and landing can be handled.
/// </summary>
[RegisterComponent]
public sealed partial class GrabThrownComponent : Component
{
    public EntityUid? Thrower;
    public int MaxCollides = 2;
    public int CollideCounter;
    public readonly HashSet<EntityUid> HitEntities = new();
}
