using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.BindableLock.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class BindableLockComponent : Component
{
    [DataField]
    public bool CanBind = true;
}