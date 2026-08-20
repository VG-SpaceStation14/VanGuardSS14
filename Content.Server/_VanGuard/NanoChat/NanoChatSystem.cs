using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Kitchen.Components;
using Content.Server.NameIdentifier;
using Content.Shared._VanGuard.NanoChat;
using Content.Shared.Database;
using Content.Shared.Kitchen;
using Content.Shared.NameIdentifier;
using Content.Shared.PDA;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._VanGuard.NanoChat;

/// <summary>
///     Server-side handling of NanoChat cards: unique number assignment,
///     tracking which PDA a card is inserted into and the microwave mishap
///     that scrambles or wipes a card's history.
/// </summary>
public sealed partial class NanoChatSystem : SharedNanoChatSystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private NameIdentifierSystem _name = default!;

    private readonly ProtoId<NameIdentifierGroupPrototype> _nameIdentifierGroup = "NanoChat";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NanoChatCardComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<NanoChatCardComponent, EntGotRemovedFromContainerMessage>(OnRemoved);

        SubscribeLocalEvent<NanoChatCardComponent, MapInitEvent>(OnCardInit);
        SubscribeLocalEvent<NanoChatCardComponent, BeingMicrowavedEvent>(OnMicrowaved);
    }

    private void OnInserted(Entity<NanoChatCardComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != PdaComponent.PdaIdSlotId)
            return;

        ent.Comp.PdaUid = args.Container.Owner;
        Dirty(ent);
    }

    private void OnRemoved(Entity<NanoChatCardComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (args.Container.ID != PdaComponent.PdaIdSlotId)
            return;

        ent.Comp.PdaUid = null;
        Dirty(ent);
    }

    private void OnCardInit(Entity<NanoChatCardComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Number != null)
            return;

        // Assign a random unique 4-digit number.
        _name.GenerateUniqueNameModifier(_nameIdentifierGroup, out var number);
        ent.Comp.Number = (uint)number;
        Dirty(ent);
    }

    private void OnMicrowaved(Entity<NanoChatCardComponent> ent, ref BeingMicrowavedEvent args)
    {
        // Skip if the entity was deleted (e.g., by the ID card system burning it).
        if (Deleted(ent) || !TryComp<MicrowaveComponent>(args.Microwave, out var micro) || micro.Broken)
            return;

        if (_random.Prob(0.10f))
        {
            // Rare lucky break: the whole history is wiped.
            ent.Comp.Messages.Clear();
            ent.Comp.Recipients.Clear();
            ent.Comp.CurrentChat = null;

            _adminLogger.Add(LogType.Action,
                LogImpact.Medium,
                $"{ToPrettyString(args.Microwave)} erased all messages on {ToPrettyString(ent)}");
        }
        else
        {
            ScrambleMessages(ent.Comp);

            _adminLogger.Add(LogType.Action,
                LogImpact.Medium,
                $"{ToPrettyString(args.Microwave)} scrambled messages on {ToPrettyString(ent)}");
        }

        Dirty(ent);
    }

    private void ScrambleMessages(NanoChatCardComponent component)
    {
        // The reassignment below can insert new message lists into the
        // dictionary, so iterate a snapshot of the keys instead.
        foreach (var recipientNumber in component.Messages.Keys.ToList())
        {
            var messages = component.Messages[recipientNumber];
            for (var i = 0; i < messages.Count; i++)
            {
                // 50% chance to scramble each individual message.
                if (!_random.Prob(0.5f))
                    continue;

                var message = messages[i];
                message.Content = ScrambleText(message.Content);
                messages[i] = message;
            }

            // 25% chance to reassign the whole conversation to a random recipient.
            if (_random.Prob(0.25f) && component.Recipients.Count > 0)
            {
                var newRecipient = _random.Pick(component.Recipients.Keys.ToList());
                if (newRecipient == recipientNumber)
                    continue;

                if (!component.Messages.ContainsKey(newRecipient))
                    component.Messages[newRecipient] = new List<NanoChatMessage>();

                component.Messages[newRecipient].AddRange(messages);
                component.Messages[recipientNumber].Clear();
            }
        }
    }

    private string ScrambleText(string text)
    {
        var chars = text.ToCharArray();

        // Fisher-Yates shuffle.
        for (var n = chars.Length; n > 1; n--)
        {
            var k = _random.Next(n);
            (chars[k], chars[n - 1]) = (chars[n - 1], chars[k]);
        }

        return new string(chars);
    }
}
