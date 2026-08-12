using Robust.Shared.Prototypes;
using Robust.Shared.Audio;

namespace Content.Shared._VanGuard.Language;

/// <summary>
///     Definition of how a language is transmitted.
///     Configures the colour, font and garbled (unintelligible) appearance of the language.
///     Concrete styles live in the server project.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class LanguageStyle
{
    public ProtoId<LanguagePrototype> Language { get; set; }

    /// <summary>
    ///     Colour applied to spoken messages in this language.
    /// </summary>
    [DataField]
    public Color? Color { get; set; }

    /// <summary>
    ///     Colour applied to whispered messages in this language.
    /// </summary>
    [DataField]
    public Color? WhisperColor { get; set; }

    /// <summary>
    ///     Whether sending a message raises <see cref="EntitySpokeEvent"/>.
    /// </summary>
    [DataField]
    public bool RaiseEvent { get; set; } = true;

    /// <summary>
    ///     Speech verbs used when a message ends with one of the known suffixes.
    /// </summary>
    [DataField("verbs")]
    public Dictionary<string, List<string>> SuffixSpeechVerbs { get; set; } = new()
    {
        { "chat-speech-verb-suffix-exclamation-strong", new() },
        { "chat-speech-verb-suffix-exclamation", new() },
        { "chat-speech-verb-suffix-question", new() },
        { "chat-speech-verb-suffix-stutter", new() },
        { "chat-speech-verb-suffix-mumble", new() },
    };

    [DataField]
    public int? FontSize { get; set; }

    [DataField]
    public string? Font { get; set; }
}

/// <summary>
///     A regular vocal language. Garbled speech is built from a list of replacement
///     syllables or phrases, or the whole message is swapped for a single phrase.
/// </summary>
public sealed partial class LinguisticStyle : LanguageStyle
{
    /// <summary>
    ///     Syllables/phrases used to build the unintelligible version of a message.
    /// </summary>
    [DataField(required: true)]
    public List<string> Replacement = new();

    /// <summary>
    ///     If true, each word is replaced with random syllables. If false, the message
    ///     is replaced sentence-by-sentence with random phrases.
    /// </summary>
    [DataField]
    public bool ObfuscateSyllables;

    /// <summary>
    ///     If true, the entire message is replaced with a single random phrase.
    /// </summary>
    [DataField]
    public bool ReplaceEntireMessage;

    /// <summary>
    ///     Syllable-based languages may want the message to be transformed per-character.
    /// </summary>
    [DataField]
    public bool PerCharacter;

    /// <summary>
    ///     List of character mappings used when <see cref="PerCharacter"/> is set.
    /// </summary>
    [DataField]
    public Dictionary<char, string> CharacterMap { get; set; } = new();
}

/// <summary>
///     A non-vocal language conveyed through body language. The garbled version is a
///     single random emote phrase, and the speaker plays a sound.
/// </summary>
public sealed partial class EmoteStyle : LanguageStyle
{
    [DataField(required: true)]
    public List<string> Replacement = new();

    [DataField]
    public SoundSpecifier? Sound;
}
