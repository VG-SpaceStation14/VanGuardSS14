using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Language;

/// <summary>
///     Sent by the client to request switching the current language.
/// </summary>
[Serializable, NetSerializable]
public sealed class LanguageChosenMessage : EntityEventArgs
{
    public NetEntity Uid;
    public string SelectedLanguage;

    public LanguageChosenMessage(NetEntity uid, string selectedLanguage)
    {
        Uid = uid;
        SelectedLanguage = selectedLanguage;
    }
}

/// <summary>
///     Sent by the server to update the language switcher menu state.
/// </summary>
[Serializable, NetSerializable]
public sealed class LanguageMenuStateMessage : EntityEventArgs
{
    public NetEntity ComponentOwner;
    public string CurrentLanguage;
    public Dictionary<string, LanguageKnowledge> Options;
    public Dictionary<string, LanguageKnowledge> TranslatorOptions;

    public LanguageMenuStateMessage(
        NetEntity componentOwner,
        string currentLanguage,
        Dictionary<string, LanguageKnowledge> options,
        Dictionary<string, LanguageKnowledge> translatorOptions)
    {
        ComponentOwner = componentOwner;
        CurrentLanguage = currentLanguage;
        Options = options;
        TranslatorOptions = translatorOptions;
    }
}
