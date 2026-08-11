using Content.Server._VanGuard.Language;
using Content.Shared._VanGuard.Language;
using Content.Shared.Chat;
using Content.Shared.Radio;
using Content.Shared.Speech;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    /// <summary>
    ///     Attempts to transmit a whispered message through the speaker's current language.
    /// </summary>
    private bool TryHandleLanguageWhisper(
        EntityUid source,
        string originalMessage,
        string message,
        ChatTransmitRange range,
        string name,
        string nameIdentity,
        RadioChannelPrototype? channel)
    {
        if (!TryComp<LanguageSpeakerComponent>(source, out _))
            return false;

        var language = _language.GetSelectedLanguage(source);
        if (language.ID == SharedLanguageSystem.UniversalLanguageId)
            return false;

        if (!_language.PassesSpeakerConditions(source, language))
            return false;

        var style = language.Style;

        var voicedMessage = _language.AccentuateMessage(source, language.ID, message);
        if (voicedMessage.Length == 0)
            return true;

        var garbledMessage = _language.ObfuscateMessage(source, message, style, _random);

        var escapedVoiced = FormattedMessage.EscapeText(voicedMessage);
        var escapedGarbled = FormattedMessage.EscapeText(garbledMessage);
        var color = style.WhisperColor;
        if (color.HasValue)
        {
            escapedVoiced = $"[color={color.Value.ToHex()}]{escapedVoiced}[/color]";
            escapedGarbled = $"[color={color.Value.ToHex()}]{escapedGarbled}[/color]";
        }

        var wrappedMessage = Loc.GetString("chat-manager-entity-whisper-wrap-message",
            ("entityName", name), ("message", escapedVoiced));

        var wrappedGarbledMessage = Loc.GetString("chat-manager-entity-whisper-wrap-message",
            ("entityName", nameIdentity), ("message", escapedGarbled));

        foreach (var (session, data) in GetRecipients(source, WhisperMuffledRange))
        {
            if (session.AttachedEntity is not { Valid: true } listener)
                continue;

            if (MessageRangeCheck(session, data, range) != MessageRangeCheckResult.Full)
                continue;

            if (!_language.PassesListenerConditions(listener, source, language))
                continue;

            var understands = _language.CanUnderstand(listener, language);
            var usePlain = data.Range <= WhisperClearRange || data.Observer;

            var outMessage = usePlain && understands ? wrappedMessage : wrappedGarbledMessage;
            _chatManager.ChatMessageToOne(ChatChannel.Whisper, message, outMessage, source, false, session.Channel);
        }

        _replay.RecordServerMessage(new ChatMessage(ChatChannel.Whisper, message, wrappedMessage, GetNetEntity(source), null, MessageRangeHideChatForReplay(range)));

        var spokeEvent = new EntitySpokeEvent(source, voicedMessage, originalMessage, channel, garbledMessage);
        RaiseLocalEvent(source, spokeEvent, true);

        return true;
    }
}
