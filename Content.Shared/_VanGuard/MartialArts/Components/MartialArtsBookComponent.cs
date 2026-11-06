using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.MartialArts;

/// <summary>
/// A one-use manual that teaches a martial art after a short reading action.
/// </summary>
[RegisterComponent]
public sealed partial class MartialArtsBookComponent : Component
{
    [DataField(required: true)]
    public ProtoId<MartialArtPrototype> Style;

    [DataField]
    public float LearnTime = 4f;

    [DataField]
    public SoundSpecifier? LearnSound;

    [DataField]
    public EntProtoId AshPrototype = "Ash";
}
