using Content.Shared.Examine;

namespace Content.Shared._VanGuard.NanoChat;

/// <summary>
///     Shared API for the NanoChat card: number management, address book and
///     message history accessors used by both the server cartridge system and
///     anything else that needs to poke at a card.
/// </summary>
public abstract class SharedNanoChatSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NanoChatCardComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<NanoChatCardComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Number is not { } number)
        {
            args.PushMarkup(Loc.GetString("nanochat-card-examine-no-number"));
            return;
        }

        args.PushMarkup(Loc.GetString("nanochat-card-examine-number", ("number", $"{number:D4}")));
    }

    #region Number

    public uint? GetNumber(Entity<NanoChatCardComponent?> card)
    {
        if (!Resolve(card, ref card.Comp))
            return null;

        return card.Comp.Number;
    }

    public void SetNumber(Entity<NanoChatCardComponent?> card, uint number)
    {
        if (!Resolve(card, ref card.Comp))
            return;

        card.Comp.Number = number;
        Dirty(card);
    }

    #endregion

    #region Recipients

    public IReadOnlyDictionary<uint, NanoChatRecipient> GetRecipients(Entity<NanoChatCardComponent?> card)
    {
        if (!Resolve(card, ref card.Comp))
            return new Dictionary<uint, NanoChatRecipient>();

        return card.Comp.Recipients;
    }

    public NanoChatRecipient? GetRecipient(Entity<NanoChatCardComponent?> card, uint number)
    {
        if (!Resolve(card, ref card.Comp) || !card.Comp.Recipients.TryGetValue(number, out var recipient))
            return null;

        return recipient;
    }

    public void SetRecipient(Entity<NanoChatCardComponent?> card, uint number, NanoChatRecipient recipient)
    {
        if (!Resolve(card, ref card.Comp))
            return;

        card.Comp.Recipients[number] = recipient;
        Dirty(card);
    }

    /// <summary>
    ///     Makes sure a conversation with the given number exists, creating the
    ///     recipient entry (when info is provided) and an empty message list.
    /// </summary>
    public bool EnsureRecipientExists(Entity<NanoChatCardComponent?> card, uint number, NanoChatRecipient? recipientInfo = null)
    {
        if (!Resolve(card, ref card.Comp))
            return false;

        if (!card.Comp.Recipients.ContainsKey(number))
        {
            if (recipientInfo == null)
                return false;

            card.Comp.Recipients[number] = recipientInfo.Value;
        }

        if (!card.Comp.Messages.ContainsKey(number))
            card.Comp.Messages[number] = new List<NanoChatMessage>();

        Dirty(card);
        return true;
    }

    #endregion

    #region Messages

    public IReadOnlyDictionary<uint, List<NanoChatMessage>> GetMessages(Entity<NanoChatCardComponent?> card)
    {
        if (!Resolve(card, ref card.Comp))
            return new Dictionary<uint, List<NanoChatMessage>>();

        return card.Comp.Messages;
    }

    public List<NanoChatMessage>? GetMessagesForRecipient(Entity<NanoChatCardComponent?> card, uint number)
    {
        if (!Resolve(card, ref card.Comp) || !card.Comp.Messages.TryGetValue(number, out var messages))
            return null;

        return messages;
    }

    public void AddMessage(Entity<NanoChatCardComponent?> card, uint number, NanoChatMessage message)
    {
        if (!Resolve(card, ref card.Comp))
            return;

        if (!card.Comp.Messages.TryGetValue(number, out var list))
        {
            list = new List<NanoChatMessage>();
            card.Comp.Messages[number] = list;
        }

        list.Add(message);
        Dirty(card);
    }

    #endregion

    #region Chat state

    public uint? GetCurrentChat(Entity<NanoChatCardComponent?> card)
    {
        if (!Resolve(card, ref card.Comp))
            return null;

        return card.Comp.CurrentChat;
    }

    public void SetCurrentChat(Entity<NanoChatCardComponent?> card, uint? number)
    {
        if (!Resolve(card, ref card.Comp) || card.Comp.CurrentChat == number)
            return;

        card.Comp.CurrentChat = number;
        Dirty(card);
    }

    public bool HasUnreadMessages(Entity<NanoChatCardComponent?> card, uint number)
    {
        if (!Resolve(card, ref card.Comp) || !card.Comp.Recipients.TryGetValue(number, out var recipient))
            return false;

        return recipient.HasUnread;
    }

    #endregion

    #region Card settings

    public bool GetNotificationsMuted(Entity<NanoChatCardComponent?> card)
    {
        if (!Resolve(card, ref card.Comp))
            return false;

        return card.Comp.NotificationsMuted;
    }

    public void SetNotificationsMuted(Entity<NanoChatCardComponent?> card, bool muted)
    {
        if (!Resolve(card, ref card.Comp) || card.Comp.NotificationsMuted == muted)
            return;

        card.Comp.NotificationsMuted = muted;
        Dirty(card);
    }

    public bool GetListNumber(Entity<NanoChatCardComponent?> card)
    {
        if (!Resolve(card, ref card.Comp))
            return true;

        return card.Comp.ListNumber;
    }

    public void SetListNumber(Entity<NanoChatCardComponent?> card, bool listNumber)
    {
        if (!Resolve(card, ref card.Comp) || card.Comp.ListNumber == listNumber)
            return;

        card.Comp.ListNumber = listNumber;
        Dirty(card);
    }

    #endregion

    #region Cleanup

    public void Clear(Entity<NanoChatCardComponent?> card)
    {
        if (!Resolve(card, ref card.Comp))
            return;

        card.Comp.Messages.Clear();
        card.Comp.Recipients.Clear();
        card.Comp.CurrentChat = null;
        Dirty(card);
    }

    public bool TryDeleteChat(Entity<NanoChatCardComponent?> card, uint number, bool keepMessages = false)
    {
        if (!Resolve(card, ref card.Comp))
            return false;

        var removed = card.Comp.Recipients.Remove(number);

        if (!keepMessages)
            card.Comp.Messages.Remove(number);

        if (card.Comp.CurrentChat == number)
            card.Comp.CurrentChat = null;

        if (removed)
            Dirty(card);

        return removed;
    }

    #endregion
}
