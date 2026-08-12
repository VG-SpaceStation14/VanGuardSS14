using Content.Server._VanGuard.Language;
using Content.Shared._VanGuard.Language;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Radio;
using Content.Shared.Speech;
using Robust.Shared.Player;
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
        RadioChannelPrototype? channel,
        bool hideLog)
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

        var wrappedUnknownMessage = Loc.GetString("chat-manager-entity-whisper-unknown-wrap-message",
            ("message", escapedGarbled));

        foreach (var (session, data) in GetRecipients(source, WhisperMuffledRange))
        {
            if (session.AttachedEntity is not { Valid: true } listener)
                continue;

            if (MessageRangeCheck(session, data, range) != MessageRangeCheckResult.Full)
                continue;

            if (!_language.PassesListenerConditions(listener, source, language))
                continue;

            var understands = _language.CanUnderstand(listener, language);

            if (data.Range <= WhisperClearRange || data.Observer)
            {
                // Close enough (or observing) to hear the message: clear text for
                // listeners who understand, garbled text with known identity otherwise.
                _chatManager.ChatMessageToOne(ChatChannel.Whisper,
                    understands ? message : garbledMessage,
                    understands ? wrappedMessage : wrappedGarbledMessage,
                    source, false, session.Channel);
            }
            else if (_examineSystem.InRangeUnOccluded(source, listener, WhisperMuffledRange))
            {
                // Muffled but the speaker is in sight: garbled text with identity.
                _chatManager.ChatMessageToOne(ChatChannel.Whisper, garbledMessage, wrappedGarbledMessage,
                    source, false, session.Channel);
            }
            else
            {
                // Muffled and out of sight: the speaker's identity is unknown.
                _chatManager.ChatMessageToOne(ChatChannel.Whisper, garbledMessage, wrappedUnknownMessage,
                    source, false, session.Channel);
            }
        }

        _replay.RecordServerMessage(new ChatMessage(ChatChannel.Whisper, message, wrappedMessage, GetNetEntity(source), null, MessageRangeHideChatForReplay(range)));

        var spokeEvent = new EntitySpokeEvent(source, voicedMessage, originalMessage, channel, garbledMessage);
        RaiseLocalEvent(source, spokeEvent, true);

        // Record the original (unobfuscated) message in the admin log, mirroring the
        // base whisper pipeline and honouring hideLog.
        if (!hideLog && HasComp<ActorComponent>(source))
        {
            if (originalMessage == message)
            {
                if (name != Name(source))
                    _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Whisper from {source} as {name}: {originalMessage}.");
                else
                    _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Whisper from {source}: {originalMessage}.");
            }
            else
            {
                if (name != Name(source))
                    _adminLogger.Add(LogType.Chat, LogImpact.Low,
                        $"Whisper from {source} as {name}, original: {originalMessage}, transformed: {message}.");
                else
                    _adminLogger.Add(LogType.Chat, LogImpact.Low,
                        $"Whisper from {source}, original: {originalMessage}, transformed: {message}.");
            }
        }

        return true;
    }
}
