using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.NanoChat;

/// <summary>
///     Marks a PDA cartridge as the NanoChat program. The cartridge reads the
///     <see cref="Content.Shared._VanGuard.NanoChat.NanoChatCardComponent"/> of the
///     ID card currently inserted in the PDA and provides its chat UI.
/// </summary>
[RegisterComponent, Access(typeof(NanoChatCartridgeSystem))]
public sealed partial class NanoChatCartridgeComponent : Component
{
    /// <summary>
    ///     Station the cartridge was last used on, used for the directory lookup.
    /// </summary>
    [DataField]
    public EntityUid? Station;

    /// <summary>
    ///     The NanoChat card currently linked to this cartridge.
    /// </summary>
    [DataField]
    public EntityUid? Card;

    /// <summary>
    ///     Radio channel whose telecomms infrastructure is used to gate delivery.
    /// </summary>
    [DataField]
    public ProtoId<RadioChannelPrototype> RadioChannel = "Common";
}
