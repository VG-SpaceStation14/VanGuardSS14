using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Grab;

/// <summary>
/// Raised on a puller when repeated pull input is used. If handled, the normal
/// pull-stop behavior is suppressed and the grab stage is advanced instead.
/// </summary>
public sealed class GrabStageToggleEvent(EntityUid puller, EntityUid pulled) : HandledEntityEventArgs
{
    public readonly EntityUid Puller = puller;
    public readonly EntityUid Pulled = pulled;
}

[ByRefEvent]
public readonly record struct GrabStageChangedEvent(EntityUid Puller, EntityUid Pulled, GrabStage OldStage, GrabStage NewStage);

[Serializable, NetSerializable]
public sealed partial class GrabEscapeDoAfterEvent : SimpleDoAfterEvent;
