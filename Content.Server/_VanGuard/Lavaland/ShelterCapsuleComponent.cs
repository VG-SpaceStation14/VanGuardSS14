using Robust.Shared.Utility;

namespace Content.Server._VanGuard.Lavaland;

[RegisterComponent]
public sealed partial class ShelterCapsuleComponent : Component
{
    [DataField]
    public ResPath GridPath = new("/Maps/_VanGuard/Lavaland/small.yml");

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(5);

    public TimeSpan? NextSpawn;
    public EntityUid? User;
}
