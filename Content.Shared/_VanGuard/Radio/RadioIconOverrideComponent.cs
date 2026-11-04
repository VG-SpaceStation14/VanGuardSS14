using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.Radio;

/// <summary>
///     Overrides the icon shown next to an entity's name in radio messages.
/// </summary>
/// <remarks>
///     Used by machines and devices that broadcast messages but do not wear an ID card.
/// </remarks>
[RegisterComponent]
public sealed partial class RadioIconOverrideComponent : Component
{
    /// <summary>
    ///     The job icon prototype used instead of the entity's ID card icon.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<JobIconPrototype> Icon = default!;
}
