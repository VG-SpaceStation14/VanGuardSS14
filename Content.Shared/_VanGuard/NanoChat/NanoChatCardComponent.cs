using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.NanoChat;

/// <summary>
///     Marks an ID card as a NanoChat card. The card holds a unique number,
///     the address book and the full message history, so chats follow the card
///     between PDAs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NanoChatCardComponent : Component
{
    /// <summary>
    ///     The unique NanoChat number assigned to this card.
    /// </summary>
    [DataField, AutoNetworkedField]
    public uint? Number;

    /// <summary>
    ///     Address book keyed by NanoChat number.
    /// </summary>
    [DataField]
    public Dictionary<uint, NanoChatRecipient> Recipients = new();

    /// <summary>
    ///     Message history keyed by the other party's NanoChat number.
    /// </summary>
    [DataField]
    public Dictionary<uint, List<NanoChatMessage>> Messages = new();

    /// <summary>
    ///     The conversation currently open in the PDA UI.
    /// </summary>
    [DataField]
    public uint? CurrentChat;

    /// <summary>
    ///     Maximum amount of conversations the card can hold.
    /// </summary>
    [DataField]
    public int MaxRecipients = 50;

    /// <summary>
    ///     When true, incoming messages don't raise PDA notifications.
    /// </summary>
    [DataField]
    public bool NotificationsMuted;

    /// <summary>
    ///     The PDA this card is currently inserted into, if any.
    /// </summary>
    [DataField]
    public EntityUid? PdaUid;

    /// <summary>
    ///     Whether this card appears in the station's NanoChat directory.
    /// </summary>
    [DataField]
    public bool ListNumber = true;
}
