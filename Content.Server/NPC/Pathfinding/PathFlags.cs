namespace Content.Server.NPC.Pathfinding;

[Flags]
public enum PathFlags : byte
{
    None = 0,

    /// <summary>
    /// Do we have any form of access.
    /// </summary>
    Access = 1 << 0,

    /// <summary>
    /// Can we pry airlocks if necessary.
    /// </summary>
    Prying = 1 << 1,

    /// <summary>
    /// Can stuff like walls be broken.
    /// </summary>
    Smashing = 1 << 2,

    /// <summary>
    /// Can we climb it like a table or railing.
    /// </summary>
    Climbing = 1 << 3,

    /// <summary>
    /// Can we open stuff that requires interaction (e.g. click-open doors).
    /// </summary>
    Interact = 1 << 4,

    /// <summary>
    /// VG-Tweak: Can we walk over <see cref="Content.Shared.NPC.PathfindingHazardComponent"/>
    /// tiles such as lava or chasms without being harmed. Grants access to
    /// <see cref="Content.Shared.NPC.PathfindingBreadcrumbFlag.Hazardous"/> nodes.
    /// </summary>
    Hazards = 1 << 5,
}
