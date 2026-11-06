using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.MartialArts;

/// <summary>
/// Grants a martial art while the clothing item is equipped.
/// </summary>
[RegisterComponent]
public sealed partial class MartialArtsClothingComponent : Component
{
    [DataField(required: true)]
    public ProtoId<MartialArtPrototype> Style;
}
