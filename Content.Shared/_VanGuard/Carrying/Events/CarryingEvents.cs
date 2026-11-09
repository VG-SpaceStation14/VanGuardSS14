using Content.Shared.Alert;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;
using Robust.Shared.GameObjects;

namespace Content.Shared._VanGuard.Carrying;

[Serializable, NetSerializable]
public sealed partial class CarryDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CarryEscapeDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// Raised when the carrier clicks the active-carry drop alert.
/// </summary>
public sealed partial class CarryDropAlertEvent : BaseAlertEvent;

/// <summary>
/// Client request to release a carried character through the Drop keybind.
/// </summary>
[Serializable, NetSerializable]
public sealed class CarryDropRequestEvent : EntityEventArgs;
