using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;
using Robust.Shared.Utility;

namespace Content.Shared._VanGuard.Language;

/// <summary>
///     A language that an entity can speak and/or understand.
///     The visual identity of a language (name, color, garbled appearance) is part of the
///     shared SS13 heritage and is configured via YAML, while the runtime behaviour is
///     defined by the server-side <see cref="LanguageStyle"/> and <see cref="LanguageCondition"/>s.
/// </summary>
[Prototype]
public sealed partial class LanguagePrototype : IPrototype, IInheritingPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <inheritdoc />
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<LanguagePrototype>))]
    public string[]? Parents { get; private set; }

    /// <inheritdoc />
    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; private set; }

    /// <summary>
    ///     Sort weight used when ordering languages in the menu. Higher comes first.
    /// </summary>
    [DataField]
    public int Priority = 1;

    /// <summary>
    ///     Whether this language can be picked in the character setup screen.
    /// </summary>
    [DataField]
    public bool Roundstart;

    /// <summary>
    ///     Whether the language is listed in the in-game language switcher once known.
    /// </summary>
    [DataField]
    public bool ShowUnderstood = true;

    /// <summary>
    ///     Whether speaking this language produces an audible voice.
    /// </summary>
    [DataField]
    public bool Vocal = true;

    /// <summary>
    ///     Color used to tint the language's name in the UI.
    /// </summary>
    [DataField]
    public Color? UiColor;

    /// <summary>
    ///     Icon displayed next to the language in UI lists.
    /// </summary>
    [DataField]
    public SpriteSpecifier Icon { get; private set; } =
        new SpriteSpecifier.Rsi(new ResPath("/Textures/_VanGuard/Interface/Chat/language.rsi"), "what");

    /// <summary>
    ///     Server-only definition of how messages in this language are transmitted
    ///     (plain speech, emotes, replacement phrases...).
    /// </summary>
    [DataField("style", serverOnly: true, required: true)]
    public LanguageStyle Style = default!;

    /// <summary>
    ///     Server-only conditions that gate who can speak or hear this language.
    /// </summary>
    [DataField("conditions", serverOnly: true)]
    public LanguageCondition[] Conditions = Array.Empty<LanguageCondition>();

    public string Name => Loc.GetString($"language-{ID}-name");
    public string Description => Loc.GetString($"language-{ID}-description");
}
