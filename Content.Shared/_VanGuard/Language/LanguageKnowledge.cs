using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Language;

/// <summary>
///     How well an entity knows a language.
/// </summary>
[Serializable, NetSerializable]
public enum LanguageKnowledge : int
{
    /// <summary>
    ///     The entity can understand the language, but not speak it.
    /// </summary>
    Understand = 0,

    /// <summary>
    ///     The entity can speak the language, but with a noticeable accent.
    /// </summary>
    BadSpeak = 1,

    /// <summary>
    ///     The entity speaks the language fluently.
    /// </summary>
    Speak = 2,
}
