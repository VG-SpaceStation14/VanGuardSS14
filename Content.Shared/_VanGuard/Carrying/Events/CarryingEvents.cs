using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Carrying;

[Serializable, NetSerializable]
public sealed partial class CarryDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CarryEscapeDoAfterEvent : SimpleDoAfterEvent;
