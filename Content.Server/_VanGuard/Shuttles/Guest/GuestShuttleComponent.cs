namespace Content.Server._VanGuard.Shuttles.Guest;

[RegisterComponent]
public sealed partial class GuestShuttleComponent : Component
{
    [ViewVariables]
    public EntityUid? Station;
}
