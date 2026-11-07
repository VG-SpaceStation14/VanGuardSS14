using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Grab;

/// <summary>
/// Server-synchronised grab strength for one puller.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GrabStageComponent : Component
{
    [AutoNetworkedField]
    public GrabStage Stage = GrabStage.None;

    [AutoNetworkedField]
    public EntityUid? Grabbing;

    public TimeSpan NextStageChange;
    public TimeSpan NextDistanceCheck;
    public TimeSpan EscapeStartedAt;
}
