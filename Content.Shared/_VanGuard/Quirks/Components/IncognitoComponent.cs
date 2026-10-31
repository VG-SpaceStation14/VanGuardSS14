using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Quirks.Components;

/// <summary>
///     Marks a character (or its mind) as incognito: the player's nickname is not revealed in the
///     round-end player list.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class IncognitoComponent : Component
{
}
