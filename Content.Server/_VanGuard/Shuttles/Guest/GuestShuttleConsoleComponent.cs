namespace Content.Server._VanGuard.Shuttles.Guest;

[RegisterComponent]
public sealed partial class GuestShuttleConsoleComponent : Component
{
    [DataField]
    public EntityUid? LinkedShuttle;
}
