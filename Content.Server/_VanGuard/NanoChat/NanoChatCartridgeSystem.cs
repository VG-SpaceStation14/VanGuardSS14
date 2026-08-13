using Content.Server.CartridgeLoader;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared._VanGuard.NanoChat;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._VanGuard.NanoChat;

/// <summary>
///     The NanoChat PDA cartridge: provides the chat UI for the card inserted
///     in the PDA and handles message delivery. Delivery is gated by the same
///     infrastructure as headsets — sender and recipient must be on the same
///     station (unless the radio channel is long range) and both stations need
///     a powered telecomms server.
/// </summary>
public sealed partial class NanoChatCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridge = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedNanoChatSystem _nanoChat = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    /// <summary>
    ///     Notification previews get cut off after this many characters.
    /// </summary>
    private const int NotificationMaxLength = 64;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NanoChatCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<NanoChatCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Keep each cartridge's card reference in sync with the PDA's ID slot.
        var query = EntityQueryEnumerator<NanoChatCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out var nanoChat, out var cartridge))
        {
            if (cartridge.LoaderUid == null || !TryComp<PdaComponent>(cartridge.LoaderUid, out var pda))
                continue;

            var newCard = pda.ContainedId;
            if (newCard == nanoChat.Card)
                continue;

            nanoChat.Card = newCard;
            UpdateUI((uid, nanoChat), cartridge.LoaderUid.Value);
        }
    }

    private void OnUiReady(Entity<NanoChatCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        UpdateUI(ent, args.Loader.Owner);
    }

    private void OnMessage(Entity<NanoChatCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not NanoChatUiMessageEvent msg)
            return;

        if (!GetCardEntity(GetEntity(msg.LoaderUid), out var card))
            return;

        switch (msg.Type)
        {
            case NanoChatUiMessageType.NewChat:
                HandleNewChat(card, msg);
                break;
            case NanoChatUiMessageType.SelectChat:
                HandleSelectChat(card, msg);
                break;
            case NanoChatUiMessageType.CloseChat:
                HandleCloseChat(card);
                break;
            case NanoChatUiMessageType.SendMessage:
                HandleSendMessage(ent, card, msg);
                break;
            case NanoChatUiMessageType.DeleteChat:
                HandleDeleteChat(card, msg);
                break;
            case NanoChatUiMessageType.ToggleMute:
                _nanoChat.SetNotificationsMuted((card, card.Comp), !_nanoChat.GetNotificationsMuted((card, card.Comp)));
                break;
            case NanoChatUiMessageType.ToggleListNumber:
                _nanoChat.SetListNumber((card, card.Comp), !_nanoChat.GetListNumber((card, card.Comp)));
                break;
        }

        UpdateUI(ent, GetEntity(msg.LoaderUid));
    }

    private bool GetCardEntity(EntityUid loaderUid, out Entity<NanoChatCardComponent> card)
    {
        card = default;

        if (!TryComp<PdaComponent>(loaderUid, out var pda)
            || pda.ContainedId is not { } contained
            || !TryComp<NanoChatCardComponent>(contained, out var nanoChat))
            return false;

        card = (contained, nanoChat);
        return true;
    }

    private void HandleNewChat(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is not { } number
            || msg.Content is not { } name
            || number == card.Comp.Number)
            return;

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        var jobTitle = string.IsNullOrWhiteSpace(msg.RecipientJob) ? null : msg.RecipientJob.Trim();

        _nanoChat.SetRecipient((card, card.Comp), number, new NanoChatRecipient(number, name, jobTitle));
        _nanoChat.SetCurrentChat((card, card.Comp), number);
    }

    private void HandleSelectChat(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is not { } number)
            return;

        _nanoChat.SetCurrentChat((card, card.Comp), number);

        if (_nanoChat.GetRecipient((card, card.Comp), number) is { } recipient)
            _nanoChat.SetRecipient((card, card.Comp), number, recipient with { HasUnread = false });
    }

    private void HandleCloseChat(Entity<NanoChatCardComponent> card)
    {
        _nanoChat.SetCurrentChat((card, card.Comp), null);
    }

    private void HandleDeleteChat(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is { } number)
            _nanoChat.TryDeleteChat((card, card.Comp), number);
    }

    private void HandleSendMessage(Entity<NanoChatCartridgeComponent> ent, Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is not { } number
            || msg.Content is not { } content
            || card.Comp.Number is not { } senderNumber)
            return;

        content = FormattedMessage.EscapeText(content.Trim());
        if (content.Length > NanoChatMessage.MaxContentLength)
            content = content[..NanoChatMessage.MaxContentLength];

        if (string.IsNullOrWhiteSpace(content))
            return;

        var message = new NanoChatMessage(_timing.CurTime, content, senderNumber);

        // Attempt delivery before stashing the message so the flag is accurate.
        var (deliveryFailed, recipients) = AttemptMessageDelivery(ent, number);
        message = message with { DeliveryFailed = deliveryFailed };

        // Store in the sender's outbox.
        _nanoChat.AddMessage((card, card.Comp), number, message);

        if (deliveryFailed)
            return;

        foreach (var recipient in recipients)
            DeliverMessageToRecipient(card, recipient, message);
    }

    /// <summary>
    ///     Looks up every card with the given number and checks which ones are
    ///     reachable from the sender's PDA (station + telecomms permitting).
    /// </summary>
    private (bool Failed, List<Entity<NanoChatCardComponent>> Recipients) AttemptMessageDelivery(
        Entity<NanoChatCartridgeComponent> sender,
        uint recipientNumber)
    {
        // Find all cards carrying this number.
        var found = new List<Entity<NanoChatCardComponent>>();
        var cardQuery = EntityQueryEnumerator<NanoChatCardComponent>();
        while (cardQuery.MoveNext(out var cardUid, out var card))
        {
            if (card.Number == recipientNumber)
                found.Add((cardUid, card));
        }

        if (found.Count == 0)
            return (true, []);

        var senderStation = _station.GetOwningStation(sender.Owner);
        if (senderStation == null || !HasActiveServer(senderStation.Value))
            return (true, []);

        var channel = _prototype.Index(sender.Comp.RadioChannel);
        var deliverable = new List<Entity<NanoChatCardComponent>>();

        foreach (var recipient in found)
        {
            // The recipient must have the NanoChat cartridge installed so we
            // can locate which station their PDA is on.
            var cartridgeQuery = EntityQueryEnumerator<NanoChatCartridgeComponent>();
            while (cartridgeQuery.MoveNext(out var cartridgeUid, out var cartridge))
            {
                if (cartridge.Card != recipient.Owner)
                    continue;

                var recipientStation = _station.GetOwningStation(cartridgeUid);
                if (recipientStation == null)
                    continue;

                if (!channel.LongRange && recipientStation != senderStation)
                    continue;

                if (!HasActiveServer(recipientStation.Value))
                    continue;

                deliverable.Add(recipient);
                break;
            }
        }

        return (deliverable.Count == 0, deliverable);
    }

    /// <summary>
    ///     Whether the given station has at least one powered telecomms server.
    /// </summary>
    private bool HasActiveServer(EntityUid station)
    {
        var query = EntityQueryEnumerator<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var power))
        {
            if (_station.GetOwningStation(uid) == station && power.Powered)
                return true;
        }

        return false;
    }

    private void DeliverMessageToRecipient(Entity<NanoChatCardComponent> sender,
        Entity<NanoChatCardComponent> recipient,
        NanoChatMessage message)
    {
        if (sender.Comp.Number is not { } senderNumber)
            return;

        // Make sure the sender is in the recipient's contacts, then store the message.
        if (!_nanoChat.EnsureRecipientExists((recipient, recipient.Comp), senderNumber, GetCardInfo(senderNumber)))
            return;

        _nanoChat.AddMessage((recipient, recipient.Comp), senderNumber, message with { DeliveryFailed = false });
        HandleUnreadNotification(recipient, message, senderNumber);
        UpdateUIForCard(recipient.Owner);
    }

    private void HandleUnreadNotification(Entity<NanoChatCardComponent> recipient,
        NanoChatMessage message,
        uint senderNumber)
    {
        var senderName = recipient.Comp.Recipients.TryGetValue(message.SenderId, out var senderRecipient)
            ? senderRecipient.Name
            : $"#{message.SenderId:D4}";

        var isCurrentChat = recipient.Comp.CurrentChat == senderNumber;

        if (!isCurrentChat)
            recipient.Comp.Recipients[message.SenderId] = senderRecipient with { HasUnread = true };

        if (recipient.Comp.NotificationsMuted
            || recipient.Comp.PdaUid is not { } pdaUid
            || !TryComp<CartridgeLoaderComponent>(pdaUid, out var loader)
            // Don't bother notifying when the recipient is actively reading this chat.
            || (isCurrentChat
                && _ui.IsUiOpen(pdaUid, PdaUiKey.Key)
                && HasComp<NanoChatCartridgeComponent>(loader.ActiveProgram)))
            return;

        _cartridge.SendNotification(pdaUid,
            Loc.GetString("nanochat-new-message-title", ("sender", senderName)),
            Loc.GetString("nanochat-new-message-body", ("message", TruncateMessage(message.Content))),
            loader);
    }

    private static string TruncateMessage(string message)
    {
        return message.Length <= NotificationMaxLength
            ? message
            : message[..(NotificationMaxLength - 4)] + " [...]";
    }

    /// <summary>
    ///     Refreshes the UI of every PDA that has a cartridge linked to the given card.
    /// </summary>
    private void UpdateUIForCard(EntityUid cardUid)
    {
        var query = EntityQueryEnumerator<NanoChatCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out var comp, out var cartridge))
        {
            if (comp.Card != cardUid || cartridge.LoaderUid == null)
                continue;

            UpdateUI((uid, comp), cartridge.LoaderUid.Value);
        }
    }

    /// <summary>
    ///     Builds and pushes the cartridge UI state for the given PDA.
    /// </summary>
    private void UpdateUI(Entity<NanoChatCartridgeComponent> ent, EntityUid loader)
    {
        List<NanoChatRecipient>? contacts = null;

        if (_station.GetOwningStation(loader) is { } station)
        {
            ent.Comp.Station = station;

            contacts = [];
            var lookup = AllEntityQuery<NanoChatCardComponent, IdCardComponent>();
            while (lookup.MoveNext(out var cardUid, out var card, out var idCard))
            {
                // Like the original VG/ADT build: any card that has an owner (a full
                // name written on its ID) and is currently on this station is listed.
                // This includes guest IDs such as the Pun Pun badge or a pet monkey's
                // card, not just IDs that were registered in the station records.
                if (!card.ListNumber || card.Number is not { } number || idCard.FullName is not { } fullName)
                    continue;

                if (_station.GetOwningStation(cardUid) != station)
                    continue;

                contacts.Add(new NanoChatRecipient(number, fullName, idCard.LocalizedJobTitle));
            }

            contacts.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        }

        Dictionary<uint, NanoChatRecipient> recipients = [];
        Dictionary<uint, List<NanoChatMessage>> messages = [];
        uint? currentChat = null;
        uint ownNumber = 0;
        var maxRecipients = 50;
        var notificationsMuted = false;
        var listNumber = true;

        if (ent.Comp.Card is { } cardEntity && TryComp<NanoChatCardComponent>(cardEntity, out var cardComp))
        {
            recipients = cardComp.Recipients;
            messages = cardComp.Messages;
            currentChat = cardComp.CurrentChat;
            ownNumber = cardComp.Number ?? 0;
            maxRecipients = cardComp.MaxRecipients;
            notificationsMuted = cardComp.NotificationsMuted;
            listNumber = cardComp.ListNumber;

            // Fill in missing job titles for existing conversations from the
            // station directory, so chats started before directory data was
            // available still show the contact's profession.
            if (contacts is { } directory)
            {
                var byNumber = new Dictionary<uint, string>();
                foreach (var entry in directory)
                {
                    if (!string.IsNullOrEmpty(entry.JobTitle))
                        byNumber[entry.Number] = entry.JobTitle;
                }

                if (byNumber.Count > 0)
                {
                    var dirty = false;
                    foreach (var (number, recipient) in cardComp.Recipients)
                    {
                        if (!string.IsNullOrEmpty(recipient.JobTitle)
                            || !byNumber.TryGetValue(number, out var job))
                            continue;

                        cardComp.Recipients[number] = recipient with { JobTitle = job };
                        dirty = true;
                    }

                    if (dirty)
                        Dirty(cardEntity, cardComp);
                }
            }
        }

        var state = new NanoChatUiState(recipients, messages, contacts, currentChat, ownNumber, maxRecipients,
            notificationsMuted, listNumber);

        _cartridge.UpdateCartridgeUiState(loader, state);
    }

    /// <summary>
    ///     Builds a recipient entry for a number, filling in the name and job
    ///     from the card's ID card component when possible.
    /// </summary>
    private NanoChatRecipient? GetCardInfo(uint number)
    {
        var query = EntityQueryEnumerator<NanoChatCardComponent>();
        while (query.MoveNext(out var uid, out var card))
        {
            if (card.Number != number)
                continue;

            var name = "Unknown";
            string? jobTitle = null;

            if (TryComp<IdCardComponent>(uid, out var idCard))
            {
                name = idCard.FullName ?? name;
                jobTitle = idCard.LocalizedJobTitle;
            }

            return new NanoChatRecipient(number, name, jobTitle);
        }

        return null;
    }
}
