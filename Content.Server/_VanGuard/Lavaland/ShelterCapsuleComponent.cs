using Robust.Shared.Utility;

namespace Content.Server._VanGuard.Lavaland;

[RegisterComponent]
public sealed partial class ShelterCapsuleComponent : Component
{
    [DataField]
    public ResPath GridPath = new("/Maps/_VanGuard/Lavaland/small.yml");

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Cooldown after a failed deployment, so the capsule cannot be spam-used
    /// while the player is standing in an invalid spot.
    /// </summary>
    [DataField]
    public TimeSpan FailureCooldown = TimeSpan.FromSeconds(5);

    public TimeSpan? NextSpawn;
    public TimeSpan? CooldownUntil;
    public EntityUid? User;
}
