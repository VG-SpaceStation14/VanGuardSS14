using System.Linq;
using Content.Server._VanGuard.Language;
using Content.Shared._VanGuard.Language;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Content.Shared.Radio;
using Content.Shared.Speech;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    [Dependency] private LanguageSystem _language = default!;

    private static readonly string[] LanguageStyleSuffixes =
    {
        "chat-speech-verb-suffix-exclamation-strong",
        "chat-speech-verb-suffix-exclamation",
        "chat-speech-verb-suffix-question",
        "chat-speech-verb-suffix-stutter",
        "chat-speech-verb-suffix-mumble",
    };

    /// <summary>
    ///     Attempts to transmit a spoken message through the speaker's current language.
    ///     Returns true when the message was handled by the language system.
    /// </summary>
    private bool TryHandleLanguageSay(
        EntityUid source,
        string originalMessage,
        string message,
        ChatTransmitRange range,
        string name,
        SpeechVerbPrototype speech,
        bool hideLog)
    {
        if (!TryComp<LanguageSpeakerComponent>(source, out _))
            return false;

        var language = _language.GetSelectedLanguage(source);
        if (language.ID == SharedLanguageSystem.UniversalLanguageId)
            return false;

        if (!_language.PassesSpeakerConditions(source, language))
            return false;

        if (language.Style is EmoteStyle emote)
            return HandleEmoteLanguage(source, message, range, name, language, emote);

        return HandleVocalLanguage(source, originalMessage, message, range, name, language, language.Style, speech);
    }

    private bool HandleVocalLanguage(
        EntityUid source,
        string originalMessage,
        string message,
        ChatTransmitRange range,
        string name,
        LanguagePrototype language,
        LanguageStyle style,
        SpeechVerbPrototype speech)
    {
        // BadSpeak speakers receive a noticeable accent.
        var voicedMessage = _language.AccentuateMessage(source, language.ID, message);
        if (voicedMessage.Length == 0)
            return true;

        var garbledMessage = _language.ObfuscateMessage(source, message, style, _random);

        // Apply language colour AFTER escaping, so markup tags are not shown literally.
        var escapedVoiced = FormattedMessage.EscapeText(voicedMessage);
        var escapedGarbled = FormattedMessage.EscapeText(garbledMessage);
        var color = style.Color;
        if (color.HasValue)
        {
            escapedVoiced = $"[color={color.Value.ToHex()}]{escapedVoiced}[/color]";
            escapedGarbled = $"[color={color.Value.ToHex()}]{escapedGarbled}[/color]";
        }

        // Resolve verbs for this style, falling back to the entity's default speech verb.
        var verbStrings = speech.SpeechVerbStrings;
        var verbsReplaced = false;
        foreach (var suffix in LanguageStyleSuffixes)
        {
            if (!message.EndsWith(Loc.GetString(suffix)) || !style.SuffixSpeechVerbs.TryGetValue(suffix, out var custom) || custom.Count == 0)
                continue;

            verbStrings = custom;
            verbsReplaced = true;
        }

        if (!verbsReplaced && style.SuffixSpeechVerbs.TryGetValue("Default", out var defaults) && defaults.Count > 0)
            verbStrings = defaults;

        var fontSize = style.FontSize ?? speech.FontSize;
        var fontId = string.IsNullOrEmpty(style.Font) ? speech.FontId : style.Font!;
        var escapedName = FormattedMessage.EscapeText(name);

        var wrappedMessage = Loc.GetString(speech.Bold ? "chat-manager-entity-say-bold-wrap-message" : "chat-manager-entity-say-wrap-message",
            ("entityName", escapedName),
            ("verb", Loc.GetString(_random.Pick(verbStrings))),
            ("fontType", fontId),
            ("fontSize", fontSize),
            ("message", escapedVoiced));

        var wrappedGarbledMessage = Loc.GetString(speech.Bold ? "chat-manager-entity-say-bold-wrap-message" : "chat-manager-entity-say-wrap-message",
            ("entityName", escapedName),
            ("verb", Loc.GetString(_random.Pick(verbStrings))),
            ("fontType", fontId),
            ("fontSize", fontSize),
            ("message", escapedGarbled));

        SendInVoiceRangeWithLanguage(ChatChannel.Local, message, wrappedMessage, wrappedGarbledMessage, source, range, language);

        // Raise the raw (unescaped, uncoloured) message so speech sounds and
        // other systems can inspect the actual punctuation and case.
        var spokeEvent = new EntitySpokeEvent(source, voicedMessage, originalMessage, null, garbledMessage);
        RaiseLocalEvent(source, spokeEvent, true);

        return true;
    }

    private bool HandleEmoteLanguage(
        EntityUid source,
        string message,
        ChatTransmitRange range,
        string name,
        LanguagePrototype language,
        EmoteStyle style)
    {
        if (!_actionBlocker.CanEmote(source))
            return true;

        var voicedMessage = _language.AccentuateMessage(source, language.ID, message);
        if (voicedMessage.Length == 0)
            return true;

        var garbledMessage = _random.Pick(style.Replacement);
        var escapedVoiced = FormattedMessage.EscapeText(voicedMessage);
        var color = style.Color;
        if (color.HasValue)
            escapedVoiced = $"[color={color.Value.ToHex()}]{escapedVoiced}[/color]";

        var escapedName = FormattedMessage.EscapeText(name);
        var verb = style.SuffixSpeechVerbs.GetValueOrDefault("Default")?.FirstOrDefault() ?? "chat-speech-verb-suffix-mumble";

        var wrappedMessage = Loc.GetString("chat-manager-entity-say-wrap-message",
            ("entityName", escapedName),
            ("verb", Loc.GetString(verb)),
            ("fontType", string.IsNullOrEmpty(style.Font) ? "Default" : style.Font!),
            ("fontSize", style.FontSize ?? 12),
            ("message", escapedVoiced));

        var wrappedGarbledMessage = Loc.GetString("chat-manager-entity-me-wrap-message",
            ("entityName", escapedName),
            ("entity", Identity.Entity(source, EntityManager)),
            ("message", FormattedMessage.RemoveMarkupOrThrow(garbledMessage)));

        SendInVoiceRangeWithLanguage(ChatChannel.Emotes, message, wrappedMessage, wrappedGarbledMessage, source, range, language);

        if (style.Sound != null)
            _audio.PlayPvs(style.Sound, source);

        return true;
    }

    /// <summary>
    ///     Transmits a message to everyone in voice range, showing either the plain or the
    ///     garbled version depending on whether each listener understands the language.
    /// </summary>
    private void SendInVoiceRangeWithLanguage(
        ChatChannel channel,
        string message,
        string wrappedMessage,
        string garbledWrappedMessage,
        EntityUid source,
        ChatTransmitRange range,
        LanguagePrototype language,
        NetUserId? author = null)
    {
        foreach (var (session, data) in GetRecipients(source, VoiceRange))
        {
            var entRange = MessageRangeCheck(session, data, range);
            if (entRange == MessageRangeCheckResult.Disallowed)
                continue;

            var entHideChat = entRange == MessageRangeCheckResult.HideChat;

            if (session.AttachedEntity is not { Valid: true } listener)
            {
                _chatManager.ChatMessageToOne(channel, message, wrappedMessage, source, entHideChat, session.Channel, author: author);
                continue;
            }

            if (!_language.PassesListenerConditions(listener, source, language))
                continue;

            var understands = _language.CanUnderstand(listener, language);
            _chatManager.ChatMessageToOne(channel, message, understands ? wrappedMessage : garbledWrappedMessage, source, entHideChat, session.Channel, author: author);
        }

        _replay.RecordServerMessage(new ChatMessage(channel, message, wrappedMessage, GetNetEntity(source), null, MessageRangeHideChatForReplay(range)));
    }
}
