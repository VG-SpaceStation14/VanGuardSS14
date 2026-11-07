namespace Content.Shared._VanGuard.Grab;

/// <summary>
/// Marks the entity currently held in a staged grab so the grab system can
/// expose stage and escape verbs without registering a second PullableComponent
/// verb handler (the upstream pull system already owns that subscription).
/// </summary>
[RegisterComponent]
public sealed partial class GrabStageTargetComponent : Component
{
    public int EscapeAttempts;
}
