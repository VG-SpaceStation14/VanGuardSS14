using Content.Shared._VanGuard.Mining.Points;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Mining.Points.Components;

/// <summary>
/// Stores mining points on a holder, such as an ID card or an ore processor.
/// Points are earned by smelting ore and can be claimed on an ID card,
/// then spent at a mining shop.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(MiningPointsSystem))]
public sealed partial class MiningPointsComponent : Component
{
    /// <summary>
    /// The number of points currently stored.
    /// </summary>
    [DataField, AutoNetworkedField]
    public uint Points;

    /// <summary>
    /// Sound played when points are successfully transferred to another holder.
    /// </summary>
    [DataField]
    public SoundSpecifier? TransferSound;
}
