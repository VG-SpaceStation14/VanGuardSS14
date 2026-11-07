using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Handover;

public enum HandoverPhase : byte
{
    Idle,
    Targeting,
    Offered,
    Accepting,
    Transferred,
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HandoverOfferComponent : Component
{
    [AutoNetworkedField]
    public bool IsTargeting;

    [AutoNetworkedField]
    public bool IsOffered;

    [AutoNetworkedField]
    public uint OfferId;

    public EntityUid? Item;
    public EntityUid? Receiver;
    public string? SourceHand;
    public TimeSpan ExpiresAt;
    public TimeSpan NextValidation;
    public bool Accepting;
}

[RegisterComponent]
public sealed partial class HandoverReceiverComponent : Component
{
    public uint OfferId;
    public EntityUid Offerer;
    public EntityUid Item;
    public TimeSpan ExpiresAt;
}

[RegisterComponent]
public sealed partial class HandoverLockComponent : Component
{
    public uint OfferId;
    public EntityUid Offerer;
    public EntityUid Receiver;
}
