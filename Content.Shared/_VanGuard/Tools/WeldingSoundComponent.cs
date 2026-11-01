using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Tools;

/// <summary>
/// Looping sound played on a tool while a welding do-after is in progress.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WeldingSoundComponent : Component
{
    [DataField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/_VanGuard/Items/welder.ogg");

    [DataField]
    public float Volume = 5f;

    public EntityUid? ActiveStream;
}
