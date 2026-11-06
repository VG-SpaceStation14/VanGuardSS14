using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.MartialArts;

public sealed partial class MartialArtActionEvent : InstantActionEvent
{
    [DataField(required: true)]
    public ProtoId<MartialArtMovePrototype> Move;
}

public sealed partial class MartialArtCycleEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class MartialArtsBookDoAfterEvent : SimpleDoAfterEvent;
