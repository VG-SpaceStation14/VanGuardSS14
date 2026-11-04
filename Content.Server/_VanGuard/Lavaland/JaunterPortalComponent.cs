namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Marks a wormhole created by the jaunter. The wormhole collapses shortly
/// after the first entity passes through it.
/// </summary>
[RegisterComponent]
public sealed partial class JaunterPortalComponent : Component
{
    /// <summary>
    /// Lifetime remaining after an entity enters the wormhole, in seconds.
    /// </summary>
    [DataField]
    public float AfterEnterLifetime = 3f;
}

/// <summary>
/// A failed jaunter wormhole. Anything that enters it is destroyed instead of
/// being teleported anywhere.
/// </summary>
[RegisterComponent]
public sealed partial class JaunterKillPortalComponent : Component;
