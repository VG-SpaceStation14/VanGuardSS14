using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.NanoChat;

/// <summary>
///     A single contact in a NanoChat card's address book.
/// </summary>
[Serializable, NetSerializable, DataRecord]
public partial struct NanoChatRecipient
{
    /// <summary>
    ///     The recipient's unique NanoChat number.
    /// </summary>
    public uint Number;

    /// <summary>
    ///     The display name, usually taken from the owner's ID card.
    /// </summary>
    public string Name;

    /// <summary>
    ///     The job title, if known.
    /// </summary>
    public string? JobTitle;

    /// <summary>
    ///     Whether there are messages from this recipient the owner hasn't read yet.
    /// </summary>
    public bool HasUnread;

    public NanoChatRecipient(uint number, string name, string? jobTitle = null, bool hasUnread = false)
    {
        Number = number;
        Name = name;
        JobTitle = jobTitle;
        HasUnread = hasUnread;
    }
}

/// <summary>
///     A single NanoChat message stored on a card.
/// </summary>
[Serializable, NetSerializable, DataRecord]
public partial struct NanoChatMessage
{
    public const int MaxContentLength = 256;

    /// <summary>
    ///     When the message was sent.
    /// </summary>
    public TimeSpan Timestamp;

    /// <summary>
    ///     The message body.
    /// </summary>
    public string Content;

    /// <summary>
    ///     NanoChat number of the sender.
    /// </summary>
    public uint SenderId;

    /// <summary>
    ///     Whether delivery failed (no matching card in range, telecomms down, etc).
    /// </summary>
    public bool DeliveryFailed;

    public NanoChatMessage(TimeSpan timestamp, string content, uint senderId, bool deliveryFailed = false)
    {
        Timestamp = timestamp;
        Content = content;
        SenderId = senderId;
        DeliveryFailed = deliveryFailed;
    }
}

/// <summary>
///     UI state pushed from the server to the NanoChat cartridge.
/// </summary>
[Serializable, NetSerializable]
public sealed class NanoChatUiState : BoundUserInterfaceState
{
    public readonly Dictionary<uint, NanoChatRecipient> Recipients;
    public readonly Dictionary<uint, List<NanoChatMessage>> Messages;
    public readonly List<NanoChatRecipient>? Contacts;
    public readonly uint? CurrentChat;
    public readonly uint OwnNumber;
    public readonly int MaxRecipients;
    public readonly bool NotificationsMuted;
    public readonly bool ListNumber;

    public NanoChatUiState(
        Dictionary<uint, NanoChatRecipient> recipients,
        Dictionary<uint, List<NanoChatMessage>> messages,
        List<NanoChatRecipient>? contacts,
        uint? currentChat,
        uint ownNumber,
        int maxRecipients,
        bool notificationsMuted,
        bool listNumber)
    {
        Recipients = recipients;
        Messages = messages;
        Contacts = contacts;
        CurrentChat = currentChat;
        OwnNumber = ownNumber;
        MaxRecipients = maxRecipients;
        NotificationsMuted = notificationsMuted;
        ListNumber = listNumber;
    }
}

/// <summary>
///     Actions the cartridge UI can request from the server.
/// </summary>
[Serializable, NetSerializable]
public enum NanoChatUiMessageType : byte
{
    NewChat,
    SelectChat,
    CloseChat,
    SendMessage,
    DeleteChat,
    ToggleMute,
    ToggleListNumber,
}

/// <summary>
///     UI message sent from the NanoChat cartridge to the server.
/// </summary>
[Serializable, NetSerializable]
public sealed class NanoChatUiMessageEvent : CartridgeMessageEvent
{
    public readonly NanoChatUiMessageType Type;
    public readonly uint? RecipientNumber;
    public readonly string? Content;
    public readonly string? RecipientJob;

    public NanoChatUiMessageEvent(NanoChatUiMessageType type, uint? recipientNumber = null, string? content = null, string? recipientJob = null)
    {
        Type = type;
        RecipientNumber = recipientNumber;
        Content = content;
        RecipientJob = recipientJob;
    }
}
