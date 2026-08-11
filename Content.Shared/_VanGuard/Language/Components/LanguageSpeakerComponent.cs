using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Language;

/// <summary>
///     Allows an entity to speak and understand languages.
///     The dictionary maps a language prototype id to the entity's knowledge level.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class LanguageSpeakerComponent : Component
{
    /// <summary>
    ///     The language currently selected for speech.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public string? CurrentLanguage;

    /// <summary>
    ///     Known languages mapped to the knowledge level of each one.
    /// </summary>
    [DataField("languages"), AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, LanguageKnowledge> Languages = new();
}
