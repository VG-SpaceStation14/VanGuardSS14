using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Ranged.Upgrades.Components;

/// <summary>
/// Component that stores and manages <see cref="GunUpgradeComponent"/> that modify a given weapon.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(GunUpgradeSystem))]
public sealed partial class UpgradeableGunComponent : Component
{
    /// <summary>
    /// ID of container that holds upgrades.
    /// </summary>
    [DataField]
    public string UpgradesContainerId = "upgrades";

    /// <summary>
    /// Whitelist which denotes the types of upgrades that can be added.
    /// </summary>
    [DataField]
    public EntityWhitelist Whitelist = new();

    /// <summary>
    /// Sound played when upgrade is inserted.
    /// </summary>
    [DataField]
    public SoundSpecifier? InsertSound = new SoundPathSpecifier("/Audio/Effects/thunk.ogg");

    /// <summary>
    /// The maximum amount of upgrades this gun can hold.
    /// </summary>
    [DataField]
    public int MaxUpgradeCount = 2;

    // VG-Tweak Start: allow stacking several upgrades of the same type on guns that opt in.
    /// <summary>
    /// When true, upgrades sharing the same tags (e.g. multiple fire rate mods) can be
    /// installed multiple times and their modifiers stack.
    /// </summary>
    [DataField]
    public bool AllowDuplicateUpgrades = false;
    // VG-Tweak End
}
