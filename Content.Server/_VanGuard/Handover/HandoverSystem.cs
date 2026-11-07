using Content.Shared._VanGuard.Handover;
using Content.Shared.ActionBlocker;
using Content.Shared.Alert;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Throwing;
using Robust.Shared.Enums;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Handover;

/// <summary>
/// Server-authoritative consent-based item handover.
/// </summary>
public sealed partial class HandoverSystem : EntitySystem
{
    private const float OfferRange = 2f;
    private const float MaxOfferSeconds = 8f;
    private const string OfferAlert = "Offer";
    private static readonly TimeSpan ValidationInterval = TimeSpan.FromSeconds(0.25f);
    private static readonly SoundPathSpecifier OfferSound = new("/Audio/Effects/thudswoosh.ogg");
    private static readonly SoundPathSpecifier SuccessSound = new("/Audio/Effects/thudswoosh.ogg");

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ISharedPlayerManager _players = default!;

    private uint _nextOfferId;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OfferItem, InputCmdHandler.FromDelegate(OnOfferKey, handle: false))
            .Register<HandoverSystem>();

        SubscribeLocalEvent<HandoverOfferComponent, UserInteractUsingEvent>(OnOfferInteract);
        SubscribeLocalEvent<HandoverOfferComponent, UseAttemptEvent>(OnOfferUseAttempt);
        SubscribeLocalEvent<HandoverOfferComponent, ThrowAttemptEvent>(OnOfferThrowAttempt);
        SubscribeLocalEvent<HandoverOfferComponent, AttackAttemptEvent>(OnOfferAttackAttempt);
        SubscribeLocalEvent<HandoverOfferComponent, MobStateChangedEvent>(OnOfferMobStateChanged);
        SubscribeLocalEvent<HandoverOfferComponent, EntityTerminatingEvent>(OnOfferTerminating);
        SubscribeLocalEvent<HandoverOfferComponent, PlayerDetachedEvent>(OnOfferPlayerDetached);
        SubscribeLocalEvent<HandoverOfferComponent, ComponentShutdown>(OnOfferShutdown);

        SubscribeLocalEvent<HandoverReceiverComponent, AcceptHandoverEvent>(OnAcceptAlert);
        SubscribeLocalEvent<HandoverReceiverComponent, MobStateChangedEvent>(OnReceiverMobStateChanged);
        SubscribeLocalEvent<HandoverReceiverComponent, EntityTerminatingEvent>(OnReceiverTerminating);
        SubscribeLocalEvent<HandoverReceiverComponent, PlayerDetachedEvent>(OnReceiverPlayerDetached);
        SubscribeLocalEvent<HandoverReceiverComponent, ComponentShutdown>(OnReceiverShutdown);

        SubscribeLocalEvent<HandoverLockComponent, GettingPickedUpAttemptEvent>(OnLockedPickupAttempt);
        SubscribeLocalEvent<HandoverLockComponent, BeforeGettingEquippedHandEvent>(OnLockedBeforeEquip);
        SubscribeLocalEvent<HandoverLockComponent, ThrowItemAttemptEvent>(OnLockedThrowItemAttempt);
        SubscribeLocalEvent<HandoverLockComponent, ShotAttemptedEvent>(OnLockedShotAttempt);
        SubscribeLocalEvent<HandoverLockComponent, HandDeselectedEvent>(OnLockedHandDeselected);
        SubscribeLocalEvent<HandoverLockComponent, ContainerGettingRemovedAttemptEvent>(OnLockedRemovalAttempt);
        SubscribeLocalEvent<HandoverLockComponent, EntGotInsertedIntoContainerMessage>(OnLockedInserted);
        SubscribeLocalEvent<HandoverLockComponent, EntRemovedFromContainerMessage>(OnLockedRemoved);
        SubscribeLocalEvent<HandoverLockComponent, EntityTerminatingEvent>(OnLockedTerminating);

        SubscribeLocalEvent<HandoverOfferComponent, StunnedEvent>(OnOfferStunned);
        SubscribeLocalEvent<HandoverReceiverComponent, StunnedEvent>(OnReceiverStunned);
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<HandoverSystem>();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<HandoverOfferComponent>();
        while (query.MoveNext(out var uid, out var offer))
        {
            if (offer.IsTargeting)
            {
                if (!ValidateTargeting(uid, offer))
                    CancelTargeting(uid, offer, "handover-item-lost");
                continue;
            }

            if (!offer.IsOffered || _timing.CurTime < offer.NextValidation)
                continue;

            offer.NextValidation = _timing.CurTime + ValidationInterval;

            if (!ValidateOffer(uid, offer))
            {
                EndOffer(uid, offer,
                    _timing.CurTime >= offer.ExpiresAt ? "handover-expired-source" : "handover-cancelled-source",
                    _timing.CurTime >= offer.ExpiresAt ? "handover-expired-target" : "handover-cancelled-target");
            }
        }
    }

    private void OnOfferKey(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { Valid: true } user)
            return;

        TryToggleTargeting(user);
    }

    public bool TryToggleTargeting(EntityUid user)
    {
        if (TryComp<HandoverReceiverComponent>(user, out var incoming) && incoming.OfferId != 0)
        {
            TryDecline(user, incoming.OfferId);
            return true;
        }

        if (TryComp<HandoverOfferComponent>(user, out var existing))
        {
            if (existing.IsOffered)
            {
                EndOffer(user, existing, "handover-cancelled-source", "handover-cancelled-target");
                return true;
            }

            existing.IsTargeting = false;
            Dirty(user, existing);
            RemComp<HandoverOfferComponent>(user);
            PopupTo(user, Loc.GetString("handover-cancelled-source"));
            PlayFeedback(user, OfferSound, -10f);
            return true;
        }

        if (HasComp<HandoverReceiverComponent>(user))
        {
            PopupTo(user, Loc.GetString("handover-target-busy"));
            return false;
        }

        if (!_blocker.CanInteract(user, null) ||
            _mobState.IsDead(user) ||
            _mobState.IsIncapacitated(user))
        {
            PopupTo(user, Loc.GetString("handover-cannot-act"));
            return false;
        }

        if (!TryComp<HandsComponent>(user, out var hands) ||
            hands.ActiveHandId is null ||
            !_hands.TryGetHeldItem((user, hands), hands.ActiveHandId, out var item))
        {
            PopupTo(user, Loc.GetString("handover-empty-hand"));
            return false;
        }

        if (HasComp<VirtualItemComponent>(item) || !HasComp<ItemComponent>(item))
        {
            PopupTo(user, Loc.GetString("handover-invalid-item"));
            return false;
        }

        var comp = EnsureComp<HandoverOfferComponent>(user);
        comp.IsTargeting = true;
        comp.IsOffered = false;
        comp.OfferId = 0;
        comp.Item = item;
        comp.SourceHand = hands.ActiveHandId;
        comp.Receiver = null;
        Dirty(user, comp);
        PopupTo(user, Loc.GetString("handover-select-target"));
        PlayFeedback(user, OfferSound, -8f);
        return true;
    }

    private void OnOfferInteract(Entity<HandoverOfferComponent> offerer, ref UserInteractUsingEvent args)
    {
        if (!offerer.Comp.IsTargeting || args.User != offerer.Owner)
            return;

        args.Handled = true;

        if (offerer.Comp.Item is not { } item ||
            args.Used != item ||
            !TryComp<HandsComponent>(offerer.Owner, out var hands) ||
            offerer.Comp.SourceHand is not { } sourceHand ||
            !_hands.TryGetHeldItem((offerer.Owner, hands), sourceHand, out var held) ||
            held != item)
        {
            CancelTargeting(offerer.Owner, offerer.Comp, "handover-item-lost");
            return;
        }

        if (!TryCreateOffer(offerer.Owner, args.Target))
        {
            // Keep targeting mode so the player can select another recipient.
            return;
        }
    }

    public bool TryCreateOffer(EntityUid offerer, EntityUid receiver)
    {
        if (!TryComp<HandoverOfferComponent>(offerer, out var comp) ||
            !comp.IsTargeting ||
            comp.Item is not { } item ||
            comp.SourceHand is not { } sourceHand)
            return false;

        if (receiver == offerer || TerminatingOrDeleted(receiver))
        {
            PopupTo(offerer, Loc.GetString("handover-invalid-target"));
            return false;
        }

        if (!TryComp<HandsComponent>(offerer, out var offererHands) ||
            !_hands.TryGetHeldItem((offerer, offererHands), sourceHand, out var held) ||
            held != item)
        {
            CancelTargeting(offerer, comp, "handover-item-lost");
            return false;
        }

        if (!TryComp<HandsComponent>(receiver, out var receiverHands) ||
            !HasComp<HumanoidProfileComponent>(receiver) ||
            !HasComp<MobStateComponent>(receiver) ||
            _mobState.IsDead(receiver) ||
            _mobState.IsIncapacitated(receiver))
        {
            PopupTo(offerer, Loc.GetString("handover-invalid-target"));
            return false;
        }

        if (!_blocker.CanInteract(receiver, null) || !_blocker.CanInteract(offerer, null))
        {
            PopupTo(offerer, Loc.GetString("handover-target-cannot-act"));
            return false;
        }

        if (!_interaction.InRangeUnobstructed(offerer, receiver, OfferRange))
        {
            PopupTo(offerer, Loc.GetString("handover-too-far"));
            return false;
        }

        if (HasComp<HandoverReceiverComponent>(receiver) ||
            HasComp<HandoverReceiverComponent>(offerer) ||
            (TryComp<HandoverOfferComponent>(receiver, out var receiverOffer) &&
             (receiverOffer.IsTargeting || receiverOffer.IsOffered)))
        {
            PopupTo(offerer, Loc.GetString("handover-target-busy"));
            return false;
        }

        if (HasComp<HandoverLockComponent>(item))
        {
            PopupTo(offerer, Loc.GetString("handover-item-busy"));
            return false;
        }

        if (!TryFindReceiveHand(receiver, item, out _, receiverHands))
        {
            PopupTo(offerer, Loc.GetString("handover-no-free-hand-source",
                ("target", Identity.Entity(receiver, EntityManager))));
            return false;
        }

        var offerId = ++_nextOfferId;
        comp.IsTargeting = false;
        comp.IsOffered = true;
        comp.OfferId = offerId;
        comp.Item = item;
        comp.Receiver = receiver;
        comp.SourceHand = sourceHand;
        comp.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(MaxOfferSeconds);
        comp.NextValidation = _timing.CurTime;
        comp.Accepting = false;
        Dirty(offerer, comp);

        var receiverComp = EnsureComp<HandoverReceiverComponent>(receiver);
        receiverComp.OfferId = offerId;
        receiverComp.Offerer = offerer;
        receiverComp.Item = item;
        receiverComp.ExpiresAt = comp.ExpiresAt;

        EnsureComp<HandoverLockComponent>(item);
        var lockComp = Comp<HandoverLockComponent>(item);
        lockComp.OfferId = offerId;
        lockComp.Offerer = offerer;
        lockComp.Receiver = receiver;

        _alerts.ShowAlert(receiver, OfferAlert);
        PopupTo(offerer, Loc.GetString("handover-offer-sent",
            ("target", Identity.Entity(receiver, EntityManager)),
            ("item", Identity.Entity(item, EntityManager))));
        PopupTo(receiver, Loc.GetString("handover-offer-received",
            ("user", Identity.Entity(offerer, EntityManager)),
            ("item", Identity.Entity(item, EntityManager))), PopupType.Medium);
        PlayFeedback(receiver, OfferSound, -6f);
        return true;
    }

    private void OnAcceptAlert(Entity<HandoverReceiverComponent> receiver, ref AcceptHandoverEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (receiver.Comp.OfferId == 0)
            return;

        // The standard alert click API carries only the alert type, not a custom
        // payload. The current receiver component is the authoritative OfferId.
        TryAccept(receiver.Owner, receiver.Comp.OfferId);
    }

    public bool TryAccept(EntityUid receiver, uint offerId)
    {
        if (!TryComp<HandoverReceiverComponent>(receiver, out var receiverComp) ||
            receiverComp.OfferId != offerId ||
            !TryGetOffer(receiverComp, out var offerer, out var offer) ||
            offer.Accepting)
        {
            return false;
        }

        if (!ValidateOffer(offerer, offer))
        {
            var expired = _timing.CurTime >= offer.ExpiresAt;
            EndOfferForReceiver(receiver, receiverComp,
                expired ? "handover-expired-source" : "handover-cancelled-source",
                expired ? "handover-expired-target" : "handover-cancelled-target");
            return false;
        }

        if (offer.Item is not { } item ||
            !TryComp<HandsComponent>(receiver, out var receiverHands))
            return false;

        // Remove the reservation before CanPickupToHand: the item lock intentionally
        // cancels GettingPickedUpAttemptEvent, which would also reject our own preflight.
        if (TryComp<HandoverLockComponent>(item, out _))
            RemComp<HandoverLockComponent>(item);

        if (!TryFindReceiveHand(receiver, item, out var receiveHand, receiverHands))
        {
            if (!Exists(item))
            {
                EndOffer(offerer, offer, "handover-item-lost", "handover-item-lost");
                return false;
            }

            RestoreLock(item, offerer, offer);
            PopupTo(receiver, Loc.GetString("handover-no-free-hand-target"));
            PopupTo(offerer, Loc.GetString("handover-no-free-hand-source",
                ("target", Identity.Entity(receiver, EntityManager))));
            return false;
        }

        offer.Accepting = true;

        var itemComp = Comp<ItemComponent>(item);
        var pickedUp = _hands.TryPickup(receiver, item, receiveHand,
            checkActionBlocker: true, animate: true, handsComp: receiverHands, item: itemComp);

        var receiverHolds = _hands.IsHolding(receiver, item);
        var sourceHolds = _hands.IsHolding(offerer, item);

        if (receiverHolds && !sourceHolds)
        {
            CompleteTransfer(offerer, offer, receiver);
            return true;
        }

        // The Standard TryPickup path can return true even when the underlying
        // container insertion failed, so verify the actual hand contents above.
        if (Exists(item) && sourceHolds)
            RestoreLock(item, offerer, offer);

        offer.Accepting = false;
        Dirty(offerer, offer);
        PopupTo(receiver, Loc.GetString("handover-transfer-failed"));
        PopupTo(offerer, Loc.GetString("handover-transfer-failed-source"));

        if (!pickedUp && !sourceHolds && !receiverHolds)
            EndOffer(offerer, offer, "handover-item-lost", "handover-item-lost");

        return false;
    }

    public bool TryDecline(EntityUid receiver, uint offerId)
    {
        if (!TryComp<HandoverReceiverComponent>(receiver, out var receiverComp) ||
            receiverComp.OfferId != offerId)
            return false;

        TryGetOffer(receiverComp, out var offerer, out var offer);
        if (offer != null)
            EndOffer(offerer, offer, "handover-declined-source", "handover-declined-target");
        else
            EndOfferForReceiver(receiver, receiverComp, "handover-declined-source", "handover-declined-target");
        return true;
    }

    private void CompleteTransfer(EntityUid offerer, HandoverOfferComponent offer, EntityUid receiver)
    {
        var item = offer.Item;
        var offerId = offer.OfferId;

        offer.IsTargeting = false;
        offer.IsOffered = false;
        offer.Item = null;
        offer.Receiver = null;
        offer.SourceHand = null;
        offer.OfferId = 0;
        offer.Accepting = false;

        if (TryComp<HandoverReceiverComponent>(receiver, out var receiverComp) &&
            receiverComp.OfferId == offerId)
        {
            receiverComp.OfferId = 0;
            RemComp<HandoverReceiverComponent>(receiver);
        }

        if (item is { } itemUid && Exists(itemUid))
            RemComp<HandoverLockComponent>(itemUid);

        // Do not clear a newer offer's alert/UI if this completion happens late.
        var ownsReceiver = !TryComp<HandoverReceiverComponent>(receiver, out var currentReceiver) ||
                           currentReceiver.OfferId == offerId;
        if (ownsReceiver)
            _alerts.ClearAlert(receiver, OfferAlert);

        RemComp<HandoverOfferComponent>(offerer);

        PopupTo(receiver, Loc.GetString("handover-accepted-target",
            ("item", item is { } i ? Identity.Name(i, EntityManager) : string.Empty)));
        PopupTo(offerer, Loc.GetString("handover-accepted-source",
            ("target", Identity.Entity(receiver, EntityManager)),
            ("item", item is { } i2 ? Identity.Name(i2, EntityManager) : string.Empty)));
        if (ownsReceiver)
            PlayFeedback(receiver, SuccessSound, 0f);
        PlayFeedback(offerer, SuccessSound, 0f);
    }

    private bool ValidateTargeting(EntityUid offerer, HandoverOfferComponent offer)
    {
        if (!offer.IsTargeting ||
            offer.Item is not { } item ||
            offer.SourceHand is not { } sourceHand ||
            TerminatingOrDeleted(offerer) ||
            TerminatingOrDeleted(item) ||
            !HasComp<ItemComponent>(item) ||
            HasComp<VirtualItemComponent>(item) ||
            _mobState.IsDead(offerer) ||
            _mobState.IsIncapacitated(offerer) ||
            !_blocker.CanInteract(offerer, null) ||
            !TryComp<HandsComponent>(offerer, out var hands) ||
            !_hands.TryGetHeldItem((offerer, hands), sourceHand, out var held) ||
            held != item)
        {
            return false;
        }

        return true;
    }

    private bool ValidateOffer(EntityUid offerer, HandoverOfferComponent offer)
    {
        if (!offer.IsOffered ||
            offer.OfferId == 0 ||
            offer.Item is not { } item ||
            offer.Receiver is not { } receiver ||
            offer.SourceHand is not { } sourceHand)
        {
            return false;
        }

        if (_timing.CurTime >= offer.ExpiresAt)
        {
            return false;
        }

        if (TerminatingOrDeleted(offerer) || TerminatingOrDeleted(receiver) || TerminatingOrDeleted(item))
        {
            return false;
        }

        if (!TryComp<HandsComponent>(offerer, out var offererHands) ||
            !TryComp<HandsComponent>(receiver, out _) ||
            _mobState.IsDead(offerer) || _mobState.IsIncapacitated(offerer) ||
            _mobState.IsDead(receiver) || _mobState.IsIncapacitated(receiver) ||
            !_blocker.CanInteract(offerer, null) ||
            !_blocker.CanInteract(receiver, null))
        {
            return false;
        }

        if (!_interaction.InRangeUnobstructed(offerer, receiver, OfferRange))
        {
            return false;
        }

        if (!_hands.TryGetHeldItem((offerer, offererHands), sourceHand, out var held) || held != item)
        {
            return false;
        }

        if (!TryComp<HandoverReceiverComponent>(receiver, out var receiverComp) ||
            receiverComp.OfferId != offer.OfferId)
        {
            return false;
        }

        if (!TryComp<HandoverLockComponent>(item, out var lockComp) ||
            lockComp.OfferId != offer.OfferId)
        {
            return false;
        }

        return true;
    }

    private bool TryGetOffer(HandoverReceiverComponent receiverComp, out EntityUid offerer, out HandoverOfferComponent offer)
    {
        offerer = receiverComp.Offerer;
        offer = null!;

        if (!Exists(offerer) ||
            !TryComp(offerer, out HandoverOfferComponent? component) ||
            component.OfferId != receiverComp.OfferId ||
            !component.IsOffered)
            return false;

        offer = component;
        return true;
    }

    /// <summary>
    /// Returns a session only while it is still attached to this exact entity.
    /// A live entity is not enough: Mind transfers detach the session before
    /// raising PlayerDetachedEvent, while the entity and its components remain valid.
    /// </summary>
    private ICommonSession? GetAttachedSession(EntityUid entity)
    {
        // TryGetSessionByEntity can return true with a null ActorComponent.PlayerSession
        // during detach/attach transitions. Its NotNullWhen annotation is not enough
        // to trust the out value here.
        if (!_players.TryGetSessionByEntity(entity, out var session) ||
            !IsSessionAttachedTo(session, entity))
            return null;

        return session;
    }

    internal static bool IsSessionAttachedTo(ICommonSession? session, EntityUid entity)
    {
        if (session == null)
            return false;

        if (session.AttachedEntity != entity)
            return false;

        if (session.Status is not (SessionStatus.Connected or SessionStatus.InGame))
            return false;

        if (session.Channel == null)
            return false;

        return true;
    }

    private void PopupTo(EntityUid recipient, string message, PopupType type = PopupType.Small)
    {
        if (GetAttachedSession(recipient) is not { } session)
            return;

        _popup.PopupEntity(message, recipient, session, type);
    }

    private void PlayFeedback(EntityUid recipient, SoundSpecifier sound, float volume)
    {
        if (GetAttachedSession(recipient) is null)
            return;

        _audio.PlayPvs(sound, recipient, AudioParams.Default.WithVolume(volume));
    }

    private void RestoreLock(EntityUid item, EntityUid offerer, HandoverOfferComponent offer)
    {
        if (!Exists(item))
            return;

        EnsureComp<HandoverLockComponent>(item);
        var restored = Comp<HandoverLockComponent>(item);
        restored.OfferId = offer.OfferId;
        restored.Offerer = offerer;
        restored.Receiver = offer.Receiver ?? default;
    }

    private bool TryFindReceiveHand(EntityUid receiver, EntityUid item, out string? hand, HandsComponent? hands = null)
    {
        hand = null;

        if (!Resolve(receiver, ref hands, false) ||
            !HasComp<ItemComponent>(item))
            return false;

        foreach (var handId in hands.SortedHands)
        {
            if (!_hands.HandIsEmpty((receiver, hands), handId))
                continue;

            if (!_hands.CanPickupToHand(receiver, item, handId,
                    checkActionBlocker: true, handsComp: hands))
                continue;

            hand = handId;
            return true;
        }

        return false;
    }

    private void OnOfferUseAttempt(Entity<HandoverOfferComponent> offerer, ref UseAttemptEvent args)
    {
        if (offerer.Comp.IsOffered && offerer.Comp.Item == args.Used)
            args.Cancel();
    }

    private void OnOfferThrowAttempt(Entity<HandoverOfferComponent> offerer, ref ThrowAttemptEvent args)
    {
        if (offerer.Comp.IsOffered && offerer.Comp.Item == args.ItemUid)
            args.Cancel();
    }

    private void OnOfferAttackAttempt(Entity<HandoverOfferComponent> offerer, ref AttackAttemptEvent args)
    {
        if (offerer.Comp.IsOffered &&
            (args.Weapon?.Owner == offerer.Comp.Item || args.Uid == offerer.Comp.Item))
            args.Cancel();
    }

    private void OnLockedPickupAttempt(Entity<HandoverLockComponent> item, ref GettingPickedUpAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnLockedRemovalAttempt(Entity<HandoverLockComponent> item, ref ContainerGettingRemovedAttemptEvent args)
    {
        // Incapacitated/dead owners must still be able to drop the item. The offer is
        // cleaned up by the mob-state/removal handlers, but we must not trap the item.
        if (Exists(item.Comp.Offerer) && _mobState.IsIncapacitated(item.Comp.Offerer))
            return;

        args.Cancel();
    }

    private void OnLockedBeforeEquip(Entity<HandoverLockComponent> item, ref BeforeGettingEquippedHandEvent args)
    {
        args.Cancelled = true;
    }

    private void OnLockedThrowItemAttempt(Entity<HandoverLockComponent> item, ref ThrowItemAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnLockedShotAttempt(Entity<HandoverLockComponent> item, ref ShotAttemptedEvent args)
    {
        args.Cancel();
    }

    private void OnLockedHandDeselected(Entity<HandoverLockComponent> item, ref HandDeselectedEvent args)
    {
        if (!TryGetOfferByItem(item.Comp, out var offerer, out var offer))
            return;

        EndOffer(offerer, offer, "handover-cancelled-source", "handover-cancelled-target");
    }

    private void OnLockedInserted(Entity<HandoverLockComponent> item, ref EntGotInsertedIntoContainerMessage args)
    {
        if (!TryGetOfferByItem(item.Comp, out var offerer, out var offer))
            return;

        EndOffer(offerer, offer, "handover-item-lost", "handover-item-lost");
    }

    private void OnLockedRemoved(Entity<HandoverLockComponent> item, ref EntRemovedFromContainerMessage args)
    {
        if (!TryGetOfferByItem(item.Comp, out var offerer, out var offer))
            return;

        EndOffer(offerer, offer, "handover-cancelled-source", "handover-cancelled-target");
    }

    private void OnLockedTerminating(Entity<HandoverLockComponent> item, ref EntityTerminatingEvent args)
    {
        if (!TryGetOfferByItem(item.Comp, out var offerer, out var offer))
            return;

        EndOffer(offerer, offer, "handover-item-lost", "handover-item-lost");
    }

    private bool TryGetOfferByItem(HandoverLockComponent lockComp, out EntityUid offerer, out HandoverOfferComponent offer)
    {
        offerer = lockComp.Offerer;
        offer = null!;

        if (!Exists(offerer) ||
            !TryComp(offerer, out HandoverOfferComponent? comp) ||
            comp.OfferId != lockComp.OfferId)
            return false;

        offer = comp;
        return true;
    }

    private void OnOfferMobStateChanged(Entity<HandoverOfferComponent> offerer, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead && args.NewMobState != MobState.Critical)
            return;

        if (offerer.Comp.IsOffered)
            EndOffer(offerer.Owner, offerer.Comp, "handover-cancelled-source", "handover-cancelled-target");
        else if (offerer.Comp.IsTargeting)
            CancelTargeting(offerer.Owner, offerer.Comp, "handover-cancelled-source");
    }

    private void OnReceiverMobStateChanged(Entity<HandoverReceiverComponent> receiver, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead || args.NewMobState == MobState.Critical)
            EndOfferForReceiver(receiver.Owner, receiver.Comp, "handover-cancelled-source", "handover-cancelled-target");
    }

    private void OnOfferTerminating(Entity<HandoverOfferComponent> offerer, ref EntityTerminatingEvent args)
    {
        if (offerer.Comp.IsOffered)
            EndOffer(offerer.Owner, offerer.Comp, "handover-cancelled-source", "handover-cancelled-target",
                removeOfferComponent: false);
    }

    private void OnOfferShutdown(Entity<HandoverOfferComponent> offerer, ref ComponentShutdown args)
    {
        if (offerer.Comp.IsOffered)
            EndOffer(offerer.Owner, offerer.Comp, "handover-cancelled-source", "handover-cancelled-target",
                removeOfferComponent: false);
    }

    private void OnReceiverShutdown(Entity<HandoverReceiverComponent> receiver, ref ComponentShutdown args)
    {
        if (receiver.Comp.OfferId != 0)
            EndOfferForReceiver(receiver.Owner, receiver.Comp, "handover-cancelled-source", "handover-cancelled-target",
                removeReceiverComponent: false);
    }

    private void OnOfferPlayerDetached(Entity<HandoverOfferComponent> offerer, ref PlayerDetachedEvent args)
    {
        if (offerer.Comp.IsOffered)
            EndOffer(offerer.Owner, offerer.Comp, "handover-cancelled-source", "handover-cancelled-target");
        else if (offerer.Comp.IsTargeting)
            CancelTargeting(offerer.Owner, offerer.Comp, "handover-cancelled-source");
    }

    private void OnReceiverPlayerDetached(Entity<HandoverReceiverComponent> receiver, ref PlayerDetachedEvent args)
    {
        EndOfferForReceiver(receiver.Owner, receiver.Comp, "handover-cancelled-source", "handover-cancelled-target");
    }

    private void OnReceiverTerminating(Entity<HandoverReceiverComponent> receiver, ref EntityTerminatingEvent args)
    {
        EndOfferForReceiver(receiver.Owner, receiver.Comp, "handover-cancelled-source", "handover-cancelled-target",
            removeReceiverComponent: false);
    }

    private void OnOfferStunned(Entity<HandoverOfferComponent> offerer, ref StunnedEvent args)
    {
        if (offerer.Comp.IsOffered)
            EndOffer(offerer.Owner, offerer.Comp, "handover-cancelled-source", "handover-cancelled-target");
        else if (offerer.Comp.IsTargeting)
            CancelTargeting(offerer.Owner, offerer.Comp, "handover-cancelled-source");
    }

    private void OnReceiverStunned(Entity<HandoverReceiverComponent> receiver, ref StunnedEvent args)
    {
        EndOfferForReceiver(receiver.Owner, receiver.Comp, "handover-cancelled-source", "handover-cancelled-target");
    }

    private void CancelTargeting(EntityUid offerer, HandoverOfferComponent comp, string locKey)
    {
        comp.IsTargeting = false;
        comp.Item = null;
        comp.SourceHand = null;
        Dirty(offerer, comp);
        RemComp<HandoverOfferComponent>(offerer);
        PopupTo(offerer, Loc.GetString(locKey));
    }

    private void EndOfferForReceiver(
        EntityUid receiver,
        HandoverReceiverComponent receiverComp,
        string sourceKey,
        string targetKey,
        bool removeReceiverComponent = true)
    {
        if (TryGetOffer(receiverComp, out var offerer, out var offer))
            EndOffer(offerer, offer, sourceKey, targetKey, removeReceiverComponent: removeReceiverComponent);
        else
            CompleteReceiverOnly(receiver, receiverComp, sourceKey, targetKey, removeReceiverComponent);
    }

    private void CompleteReceiverOnly(
        EntityUid receiver,
        HandoverReceiverComponent receiverComp,
        string sourceKey,
        string targetKey,
        bool removeReceiverComponent = true)
    {
        var item = receiverComp.Item;
        var offerId = receiverComp.OfferId;

        _alerts.ClearAlert(receiver, OfferAlert);
        receiverComp.OfferId = 0;
        if (removeReceiverComponent)
            RemComp<HandoverReceiverComponent>(receiver);

        if (Exists(item) &&
            TryComp<HandoverLockComponent>(item, out var lockComp) &&
            lockComp.OfferId == offerId)
            RemComp<HandoverLockComponent>(item);

        if (receiverComp.Offerer != default)
        {
            PopupTo(receiver, Loc.GetString(targetKey));
            PlayFeedback(receiver, OfferSound, -10f);
            if (Exists(receiverComp.Offerer))
            {
                PopupTo(receiverComp.Offerer, Loc.GetString(sourceKey));
                PlayFeedback(receiverComp.Offerer, OfferSound, -10f);
            }
        }
    }

    private void EndOffer(
        EntityUid offerer,
        HandoverOfferComponent offer,
        string sourceKey,
        string targetKey,
        bool removeReceiverComponent = true,
        bool removeOfferComponent = true)
    {
        if (offer.OfferId == 0)
            return;

        var offerId = offer.OfferId;
        var item = offer.Item;
        var receiver = offer.Receiver;

        offer.IsTargeting = false;
        offer.IsOffered = false;
        offer.Accepting = false;
        offer.OfferId = 0;
        offer.Item = null;
        offer.Receiver = null;
        offer.SourceHand = null;

        var ownsReceiver = false;
        if (receiver is { } receiverUid && Exists(receiverUid))
        {
            ownsReceiver = !TryComp<HandoverReceiverComponent>(receiverUid, out var receiverComp) ||
                           receiverComp.OfferId == offerId;

            if (ownsReceiver)
            {
                if (receiverComp != null)
                    receiverComp.OfferId = 0;

                _alerts.ClearAlert(receiverUid, OfferAlert);

                if (receiverComp != null && removeReceiverComponent)
                    RemComp<HandoverReceiverComponent>(receiverUid);
            }
        }

        if (item is { } itemUid && Exists(itemUid) &&
            TryComp<HandoverLockComponent>(itemUid, out var lockComp) &&
            lockComp.OfferId == offerId)
            RemComp<HandoverLockComponent>(itemUid);

        if (Exists(offerer) && removeOfferComponent)
            RemComp<HandoverOfferComponent>(offerer);

        // Notifications are strictly after reservation and component cleanup.
        // A detached/transferred participant no longer has a valid personal
        // network recipient, while the other participant may still be active.
        if (ownsReceiver && receiver is { } receiverForNotice && Exists(receiverForNotice))
        {
            PopupTo(receiverForNotice, Loc.GetString(targetKey));
            PlayFeedback(receiverForNotice, OfferSound, -10f);
        }

        if (Exists(offerer))
        {
            PopupTo(offerer, Loc.GetString(sourceKey));
            PlayFeedback(offerer, OfferSound, -10f);
        }
    }
}
