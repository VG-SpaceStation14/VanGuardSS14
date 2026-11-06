using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.MartialArts;

/// <summary>
/// Server-owned martial arts progression and the currently active style.
/// </summary>
[RegisterComponent]
public sealed partial class MartialArtsKnowledgeComponent : Component
{
    [DataField]
    public HashSet<ProtoId<MartialArtPrototype>> KnownStyles = new();

    [DataField]
    public HashSet<ProtoId<MartialArtPrototype>> TemporaryStyles = new();

    [DataField]
    public ProtoId<MartialArtPrototype>? CurrentStyle;

    [DataField]
    public ProtoId<MartialArtMovePrototype>? PreparedMove;

    public TimeSpan PreparedUntil;
    public TimeSpan NextComboAt;
    public TimeSpan CounterExpires;
    public int Counter;

    public readonly Dictionary<ProtoId<MartialArtMovePrototype>, TimeSpan> MoveCooldowns = new();
    public readonly List<MartialArtsComboEntry> History = new();
    public readonly Dictionary<ProtoId<MartialArtMovePrototype>, EntityUid> GrantedActions = new();
    public EntityUid? CycleAction;
}

public readonly record struct MartialArtsComboEntry(MartialArtActionType Action, EntityUid Target, TimeSpan At);
