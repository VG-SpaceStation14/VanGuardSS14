using System.Text;
using Content.Shared._VanGuard.Language;
using Robust.Shared.Random;

namespace Content.Server._VanGuard.Language;

public sealed partial class LanguageSystem
{
    /// <summary>
    ///     Builds the unintelligible version of a message according to the language style.
    ///     The result is stable for one message inside one round.
    /// </summary>
    public string ObfuscateMessage(EntityUid uid, string message, LanguageStyle style, IRobustRandom random)
    {
        if (style is not LinguisticStyle linguistic || linguistic.Replacement.Count == 0)
        {
            if (style is EmoteStyle emote && emote.Replacement.Count > 0)
                return random.Pick(emote.Replacement);

            return "*incomprehensible*";
        }

        if (linguistic.ReplaceEntireMessage)
        {
            var phrase = random.Pick(linguistic.Replacement);
            return phrase + ExtractTrailingPunctuation(message);
        }

        if (linguistic.PerCharacter)
        {
            var builder = new StringBuilder();
            foreach (var ch in message)
            {
                if (linguistic.CharacterMap.TryGetValue(char.ToLowerInvariant(ch), out var mapped))
                {
                    builder.Append(mapped);
                    continue;
                }

                builder.Append(random.Pick(linguistic.Replacement));
            }

            return builder.ToString();
        }

        if (linguistic.ObfuscateSyllables)
            return ObfuscateBySyllables(message, linguistic.Replacement);

        return ObfuscateByPhrases(message, linguistic.Replacement);
    }

    private static string ExtractTrailingPunctuation(string message)
    {
        var end = message.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(message[end]))
            end--;

        var result = new StringBuilder();
        for (var i = end; i >= 0; i--)
        {
            var ch = message[i];
            if (ch is '.' or ',' or '!' or '?')
                result.Append(ch);
            else
                break;
        }

        if (result.Length == 0)
            result.Append('.');

        // Punctuation was appended in reverse order.
        return Reverse(result.ToString());
    }

    private static string Reverse(string value)
    {
        var chars = value.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    private string ObfuscateBySyllables(string message, List<string> replacement)
    {
        var builder = new StringBuilder();
        var wordStart = 0;
        var wordHash = 0;

        for (var i = 0; i <= message.Length; i++)
        {
            var ch = i < message.Length ? char.ToLowerInvariant(message[i]) : '\0';
            var isEnd = i == message.Length || char.IsWhiteSpace(ch) || IsPunctuation(ch);

            if (!isEnd)
            {
                wordHash = wordHash * 31 + ch;
                continue;
            }

            if (i > wordStart)
            {
                var syllables = PseudoRandom(wordHash, 1, 3);
                for (var j = 0; j < syllables; j++)
                {
                    var index = PseudoRandom(wordHash + j, 0, replacement.Count - 1);
                    builder.Append(replacement[index]);
                }
            }

            if (i < message.Length)
                builder.Append(ch);

            wordStart = i + 1;
            wordHash = 0;
        }

        return builder.ToString();
    }

    private string ObfuscateByPhrases(string message, List<string> replacement)
    {
        var builder = new StringBuilder();
        var sentenceStart = 0;
        var sentenceHash = 0;

        for (var i = 0; i < message.Length; i++)
        {
            var ch = char.ToLowerInvariant(message[i]);
            if (ch is not ('.' or '!' or '?') && i != message.Length - 1)
            {
                sentenceHash = sentenceHash * 31 + ch;
                continue;
            }

            var length = i - sentenceStart + 1;
            if (length > 0)
            {
                var phrases = Math.Clamp((int)Math.Sqrt(length) - 1, 1, 4);
                for (var j = 0; j < phrases; j++)
                {
                    var index = PseudoRandom(sentenceHash + j, 0, replacement.Count - 1);
                    builder.Append(replacement[index]);
                    if (j != phrases - 1)
                        builder.Append(' ');
                }
            }

            sentenceStart = i + 1;
            sentenceHash = 0;

            if (ch is '.' or '!' or '?')
                builder.Append(ch).Append(' ');
        }

        return builder.ToString().TrimEnd();
    }

    private static bool IsPunctuation(char ch)
    {
        return ch is '.' or ',' or '!' or '?' or ':' or ';';
    }

    /// <summary>
    ///     Applies a mild accent for entities that only have <see cref="LanguageKnowledge.BadSpeak"/>
    ///     knowledge of the language.
    /// </summary>
    public string AccentuateMessage(EntityUid uid, string lang, string message)
    {
        if (!RetrieveKnownLanguages(uid, LanguageKnowledge.BadSpeak, out var langs, out _))
            return message;

        if (!langs.TryGetValue(lang, out var knowledge))
            return message;

        if (knowledge > LanguageKnowledge.BadSpeak)
            return message;

        var builder = new StringBuilder();
        foreach (var character in message)
        {
            if (_random.Prob(0.2f / 3f))
            {
                var lower = char.ToLowerInvariant(character);
                var mapped = lower switch
                {
                    'o' => "u",
                    's' => "ch",
                    'a' => "ah",
                    'u' => "oo",
                    'c' => "k",
                    'о' => "а",
                    'к' => "кх",
                    'щ' => "шч",
                    'ц' => "тс",
                    _ => character.ToString(),
                };
                builder.Append(mapped);
            }

            if (!_random.Prob(0.5f * 3 / 20))
            {
                builder.Append(character);
                continue;
            }

            var emphasis = _random.Next(1, 3) switch
            {
                1 => "'",
                2 => $"{character}{character}",
                _ => $"{character}{character}{character}",
            };
            builder.Append(emphasis);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Evaluates the non-listener conditions of a language against the speaker.
    /// </summary>
    public bool PassesSpeakerConditions(EntityUid speaker, LanguagePrototype language)
    {
        foreach (var condition in language.Conditions)
        {
            if (condition.CheckListener)
                continue;

            if (!condition.Evaluate(speaker, speaker, EntityManager))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Evaluates the listener conditions of a language against one listener.
    /// </summary>
    public bool PassesListenerConditions(EntityUid listener, EntityUid source, LanguagePrototype language)
    {
        foreach (var condition in language.Conditions)
        {
            if (!condition.CheckListener)
                continue;

            if (!condition.Evaluate(listener, source, EntityManager))
                return false;
        }

        return true;
    }

    private int PseudoRandom(int seed, int min, int max)
    {
        seed += Seed;
        var value = ((seed * 1103515245) + 12345) & 0x7fffffff;
        var span = max - min + 1;
        return min + (int)((uint)value % (uint)span);
    }
}
