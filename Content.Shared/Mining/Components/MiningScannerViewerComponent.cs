using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
// VG-Tweak Start
using Content.Shared._VanGuard.Mining;
// VG-Tweak End

namespace Content.Shared.Mining.Components;

// VG-Tweak Start: SharedInnateMiningScannerSystem grants/restores this viewer for innate mining vision.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause,
    Access(typeof(MiningScannerSystem), typeof(SharedInnateMiningScannerSystem))]
// VG-Tweak End
public sealed partial class MiningScannerViewerComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public float ViewRange;

    [DataField, AutoNetworkedField]
    public float AnimationDuration = 1.5f;

    [DataField, AutoNetworkedField]
    public TimeSpan PingDelay = TimeSpan.FromSeconds(5);

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextPingTime = TimeSpan.MaxValue;

    [DataField]
    public EntityCoordinates? LastPingLocation;

    [DataField, AutoNetworkedField]
    public SoundSpecifier? PingSound = new SoundPathSpecifier("/Audio/Machines/sonar-ping.ogg")
    {
        Params = new AudioParams
        {
            Volume = -3,
        }
    };

    [DataField]
    public bool QueueRemoval;
}
