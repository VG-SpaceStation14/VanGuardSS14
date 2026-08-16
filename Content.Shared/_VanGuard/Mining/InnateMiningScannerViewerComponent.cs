using Content.Shared.Mining.Components;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Mining;

/// <summary>
/// Grants the entity innate ore-detection vision: it receives (and keeps) a
/// <see cref="MiningScannerViewerComponent"/> even without a handheld mineral scanner.
/// Intended for species or entities that can naturally sense mineral veins, e.g. dwarves.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SharedInnateMiningScannerSystem))]
public sealed partial class InnateMiningScannerViewerComponent : Component
{
    /// <summary>
    /// Detection range in meters around the entity.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public float ViewRange = 5f;

    /// <summary>
    /// How often the ore overlay refreshes (the "ping").
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan PingDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Sound played on every ping. Null for silent vision.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier? PingSound;

    /// <summary>
    /// Duration of the overlay fade-in animation.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float AnimationDuration = 1.5f;
}
