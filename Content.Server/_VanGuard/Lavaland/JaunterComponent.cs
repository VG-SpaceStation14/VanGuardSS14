namespace Content.Server._VanGuard.Lavaland;

[RegisterComponent]
public sealed partial class JaunterComponent : Component
{
    [DataField]
    public bool DeleteOnUse = true;

    [DataField]
    public int Uses = 1;
}
