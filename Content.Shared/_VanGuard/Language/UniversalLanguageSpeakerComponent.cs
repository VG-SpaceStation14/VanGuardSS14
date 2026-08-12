using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Language;

/// <summary>
///     Marker component granting the ability to understand and speak every language.
///     Used for ghosts, observers and special event entities.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class UniversalLanguageSpeakerComponent : Component
{
    [DataField]
    public bool Enabled = true;
}
