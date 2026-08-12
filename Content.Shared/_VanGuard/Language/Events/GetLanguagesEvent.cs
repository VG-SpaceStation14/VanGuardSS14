using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.Language;

/// <summary>
///     Raised to collect every language an entity currently knows, including
///     languages granted by implants or other equipment.
/// </summary>
public sealed class GetLanguagesEvent : EntityEventArgs
{
    /// <summary>
    ///     Currently selected language id, if any.
    /// </summary>
    public string? Current;

    /// <summary>
    ///     All languages known directly by the entity.
    /// </summary>
    public Dictionary<string, LanguageKnowledge> Languages = new();

    /// <summary>
    ///     Languages granted by translators and other sources, mapped to their knowledge level.
    /// </summary>
    public Dictionary<string, LanguageKnowledge> Translator = new();
}
