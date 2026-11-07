namespace Content.Shared._VanGuard.Grab;

/// <summary>
/// Prevents increasing a grab stage while equipped or present on a mob.
/// </summary>
[RegisterComponent]
public sealed partial class GrabProtectionComponent : Component;
