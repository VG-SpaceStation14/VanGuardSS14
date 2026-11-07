namespace Content.Shared._VanGuard.Grab;

/// <summary>
/// The strength of a pull on a mob. None is an ordinary pull, soft/hard are
/// deliberate grabs, and choke is the strongest control stage.
/// </summary>
public enum GrabStage : byte
{
    None = 0,
    Soft = 1,
    Hard = 2,
    Choke = 3,
}
