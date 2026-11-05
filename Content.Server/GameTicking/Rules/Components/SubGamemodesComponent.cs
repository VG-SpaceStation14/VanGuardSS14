using Content.Server.GameTicking.Rules;
using Content.Shared.Storage;

namespace Content.Server.GameTicking.Rules.Components;

/// <summary>
/// When this gamerule is added it has a chance of adding other gamerules.
/// Since it's done when added and not when started you can still use normal start logic.
/// Used for starting subgamemodes in game presets.
/// </summary>
[RegisterComponent, Access(typeof(SubGamemodesSystem))]
public sealed partial class SubGamemodesComponent : Component
{
    /// <summary>
    /// Spawn entries for each gamerule prototype.
    /// Use orGroups if you want to limit rules.
    /// </summary>
    [DataField(required: true)]
    public List<EntitySpawnEntry> Rules = new();

    // VG-Tweak Start: allow presets to keep additional antagonists out of low-pop rounds
    /// <summary>
    /// Minimum number of ready players required before any subgamemode is added.
    /// </summary>
    [DataField]
    public int MinPlayers;
    // VG-Tweak End
}
