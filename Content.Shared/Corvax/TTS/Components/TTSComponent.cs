using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Corvax.TTS;

/// <summary>
/// Apply TTS for entity chat say messages
/// </summary>
[RegisterComponent, NetworkedComponent]
// ReSharper disable once InconsistentNaming
public sealed partial class TTSComponent : Component
{
    /// <summary>
    /// Prototype of used voice for TTS.
    /// </summary>
    [DataField("voice")]
    // VG-Tweak Start
    public ProtoId<TTSVoicePrototype>? VoicePrototypeId { get; set; } = "RuAlexandr";
    // VG-Tweak End
    // public ProtoId<TTSVoicePrototype>? VoicePrototypeId { get; set; } = "Taskmaster"; // VG-Tweak

    // VG-Tweak Start
    /// <summary>
    /// Effect applied to the voice (e.g., "Robot", "Echo").
    /// </summary>
    [DataField("effect")]
    public string Effect { get; set; } = string.Empty;
    // VG-Tweak End
}
