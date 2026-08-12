using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Paper;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PaperComponent : Component
{
    public PaperAction Mode;
    [DataField("content"), AutoNetworkedField]
    public string Content { get; set; } = "";

    /// <summary>
    ///     The language used by each part of the document. Empty for legacy documents.
    /// </summary>
    [DataField("languageSegments"), AutoNetworkedField]
    public List<PaperTextSegment> LanguageSegments { get; set; } = new();

    [DataField("contentSize")]
    public int ContentSize { get; set; } = 10000;

    [DataField("stampedBy"), AutoNetworkedField]
    public List<StampDisplayInfo> StampedBy { get; set; } = new();

    /// <summary>
    ///     Stamp to be displayed on the paper, state from bureaucracy.rsi
    /// </summary>
    [DataField("stampState"), AutoNetworkedField]
    public string? StampState { get; set; }

    // VG-Tweak Start
    [DataField("stampTint"), AutoNetworkedField]
    public Color StampTint { get; set; } = Color.White;
    // VG-Tweak End

    [DataField, AutoNetworkedField]
    public bool EditingDisabled;

    /// <summary>
    /// Sound played after writing to the paper.
    /// </summary>
    [DataField("sound")]
    public SoundSpecifier? Sound { get; private set; } = new SoundCollectionSpecifier("PaperScribbles", AudioParams.Default.WithVariation(0.1f));

    [Serializable, NetSerializable]
    public sealed class PaperBoundUserInterfaceState : BoundUserInterfaceState
    {
        public readonly string Text;
        public readonly List<PaperTextSegment> LanguageSegments;
        public readonly List<StampDisplayInfo> StampedBy;
        public readonly PaperAction Mode;

        public PaperBoundUserInterfaceState(string text, List<PaperTextSegment> languageSegments, List<StampDisplayInfo> stampedBy, PaperAction mode = PaperAction.Read)
        {
            Text = text;
            LanguageSegments = languageSegments;
            StampedBy = stampedBy;
            Mode = mode;
        }
    }

    [Serializable, NetSerializable]
    public sealed class PaperInputTextMessage : BoundUserInterfaceMessage
    {
        public readonly string Text;
        public readonly List<PaperTextSegment> LanguageSegments;

        public PaperInputTextMessage(string text, List<PaperTextSegment>? languageSegments = null)
        {
            Text = text;
            LanguageSegments = languageSegments ?? new();
        }
    }

    [DataDefinition, Serializable, NetSerializable]
    public sealed partial class PaperTextSegment
    {
        public string Text;
        public string Language;
        public string ObfuscatedText;

        public PaperTextSegment(string text, string language, string obfuscatedText = "")
        {
            Text = text;
            Language = language;
            ObfuscatedText = obfuscatedText;
        }
    }

    [Serializable, NetSerializable]
    public enum PaperUiKey
    {
        Key
    }

    [Serializable, NetSerializable]
    public enum PaperAction
    {
        Read,
        Write,
    }

    [Serializable, NetSerializable]
    public enum PaperVisuals : byte
    {
        Status,
        Stamp,
        StampTint // VG-Tweak
    }

    [Serializable, NetSerializable]
    public enum PaperStatus : byte
    {
        Blank,
        Written
    }
}
