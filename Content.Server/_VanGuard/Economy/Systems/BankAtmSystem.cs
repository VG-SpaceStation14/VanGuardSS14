using Content.Server._VanGuard.Economy.Components;
using Content.Shared.Access.Components;
using Content.Shared._VanGuard.Economy;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Server._VanGuard.Economy.Systems;

public sealed partial class BankAtmSystem : EntitySystem
{
    [Dependency] private EconomyBankSystem _bank = default!;
    [Dependency] private EconomyBankTransactionSystem _transactions = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly SoundSpecifier AuthSound =
        new SoundPathSpecifier("/Audio/Effects/chime.ogg");

    private static readonly SoundSpecifier ErrorSound =
        new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg");

    private static readonly SoundSpecifier ConfirmSound =
        new SoundPathSpecifier("/Audio/Machines/twobeep.ogg");

    private static readonly SoundSpecifier DepositSound =
        new SoundPathSpecifier("/Audio/Effects/rustle1.ogg");

    private static readonly SoundSpecifier WithdrawSound =
        new SoundPathSpecifier("/Audio/Machines/machine_vend.ogg");

    private readonly HashSet<(EntityUid Atm, EntityUid User)> _sessions = new();
    private readonly Dictionary<EntityUid, EntityUid> _openUsers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BankAtmComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<BankAtmComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<BankAtmComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<BankAtmComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<BankAtmComponent, BoundUIClosedEvent>(OnClosed);
        SubscribeLocalEvent<BankAtmComponent, BankAtmAuthMessage>(OnAuth);
        SubscribeLocalEvent<BankAtmComponent, BankAtmEjectMessage>(OnEject);
        SubscribeLocalEvent<BankAtmComponent, BankAtmEndSessionMessage>(OnEndSession);
        SubscribeLocalEvent<BankAtmComponent, BankAtmTransactionMessage>(OnTransaction);
        SubscribeLocalEvent<BankAtmComponent, ItemSlotInsertAttemptEvent>(OnCardInsertAttempt);
        SubscribeLocalEvent<BankAtmComponent, EntInsertedIntoContainerMessage>(OnCardInserted);
        SubscribeLocalEvent<BankAtmComponent, EntRemovedFromContainerMessage>(OnCardRemoved);
    }

    private void OnInit(Entity<BankAtmComponent> ent, ref ComponentInit args)
    {
        _itemSlots.AddItemSlot(ent.Owner, BankAtmComponent.CardSlotId, ent.Comp.CardSlot);
    }

    private void OnTerminating(Entity<BankAtmComponent> ent, ref EntityTerminatingEvent args)
    {
        // Container shutdown during entity deletion would otherwise delete the
        // card before ComponentRemove runs. Release it while the container is
        // still available.
        if (ent.Comp.CardSlot.Item is { } card)
            DropCard(ent, card);
    }

    private void OnRemove(Entity<BankAtmComponent> ent, ref ComponentRemove args)
    {
        Log.Debug($"Bank ATM {ent.Owner}: BankAtm component removing; slot item=" +
                 $"{ent.Comp.CardSlot.Item?.ToString() ?? "empty"}.");
        // RemoveItemSlot shuts the backing ContainerSlot down, and a
        // ContainerSlot deletes its contained entity on shutdown. Do not rely
        // on TryEject here: during entity termination it is rejected by the
        // container system, and the card would be deleted with the slot.
        if (ent.Comp.CardSlot.Item is { } card)
            DropCard(ent, card);

        _itemSlots.RemoveItemSlot(ent.Owner, ent.Comp.CardSlot);
    }

    private void DropCard(Entity<BankAtmComponent> ent, EntityUid card)
    {
        if (ent.Comp.CardSlot.ContainerSlot is not { } container)
        {
            Log.Error($"Bank ATM {ent.Owner} lost its card container while holding {ToPrettyString(card)}.");
            return;
        }

        var xform = Transform(ent);
        if ((xform.GridUid is { } grid && grid != EntityUid.Invalid && Terminating(grid))
            || (xform.MapUid is { } map && Terminating(map)))
        {
            // The whole map is being torn down. There is nowhere useful to
            // drop the card, and re-parenting to a terminating grid is invalid.
            Log.Debug($"Bank ATM {ent.Owner}: map is terminating; not re-parenting card " +
                      $"{ToPrettyString(card)}.");
            return;
        }

        var coords = xform.Coordinates;
        Log.Debug($"Bank ATM {ent.Owner}: dropping card {ToPrettyString(card)} " +
                  $"from slot during removal; parent before={Transform(card).ParentUid}.");

        // force + reparent false + explicit destination is the only form of
        // container removal that is allowed while the ATM is terminating.
        if (!_containers.Remove((card, null, null), container, force: true,
                reparent: false, destination: coords))
        {
            Log.Error($"Bank ATM {ent.Owner} failed to release card {ToPrettyString(card)}; " +
                      "the slot container will delete it.");
            return;
        }

        Log.Info($"Bank ATM {ent.Owner}: card {ToPrettyString(card)} released; " +
                  $"parent after={Transform(card).ParentUid}.");
    }

    private void OnOpened(Entity<BankAtmComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        _openUsers[ent.Owner] = user;
        SendUnauthorized(ent, user,
            ent.Comp.CardSlot.Item == null
                ? BankTransactionStatus.NoCard
                : BankTransactionStatus.InvalidRequest);
    }

    private void OnClosed(Entity<BankAtmComponent> ent, ref BoundUIClosedEvent args)
    {
        _openUsers.Remove(ent.Owner);
        _sessions.Remove((ent.Owner, args.Actor));
    }

    private void OnAuth(Entity<BankAtmComponent> ent, ref BankAtmAuthMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (!TryGetCardAccount(ent, user, out var account))
        {
            _audio.PlayPvs(ErrorSound, ent.Owner, AudioParams.Default.WithVolume(-2f));
            SendUnauthorized(ent, user, BankTransactionStatus.CardMismatch);
            return;
        }

        if (args.Pin != account.Comp.Pin)
        {
            _audio.PlayPvs(ErrorSound, ent.Owner, AudioParams.Default.WithVolume(-2f));
            SendUnauthorized(ent, user, BankTransactionStatus.WrongPin);
            return;
        }

        _sessions.Add((ent.Owner, user));
        _audio.PlayPvs(AuthSound, ent.Owner, AudioParams.Default.WithVolume(-2f));
        _ui.SetUiState(ent.Owner, BankAtmUiKey.Key,
            _transactions.BuildState(account, authorized: true, cardOwnerName: account.Comp.OwnerName,
                cash: _transactions.GetHeldCash(user)));
    }

    private void OnEject(Entity<BankAtmComponent> ent, ref BankAtmEjectMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        _sessions.Remove((ent.Owner, user));
        var hadCard = ent.Comp.CardSlot.Item is not null;
        if (hadCard)
        {
            Log.Debug($"Bank ATM {ent.Owner}: ejecting card {ToPrettyString(ent.Comp.CardSlot.Item)} " +
                      $"for {ToPrettyString(user)}; parent before={Transform(ent.Comp.CardSlot.Item!.Value).ParentUid}.");
        }

        // TryEjectToHands uses the standard item-slot path and places the card
        // into the player's hands (or drops it there if the hands are full).
        // TryEject alone would only reparent it to the ATM's tile, which can
        // leave it inside a wall or otherwise hard to reach.
        if (!_itemSlots.TryEjectToHands(ent.Owner, ent.Comp.CardSlot, user))
        {
            if (hadCard)
                Log.Warning($"Bank ATM {ent.Owner}: card could not be ejected for {ToPrettyString(user)}.");
            SendUnauthorized(ent, user, hadCard
                ? BankTransactionStatus.InvalidRequest
                : BankTransactionStatus.NoCard);
            return;
        }

        Log.Debug($"Bank ATM {ent.Owner}: card ejected for {ToPrettyString(user)}; " +
                  $"slot now={ent.Comp.CardSlot.Item?.ToString() ?? "empty"}.");
        SendUnauthorized(ent, user, BankTransactionStatus.NoCard);
    }

    private void OnEndSession(Entity<BankAtmComponent> ent, ref BankAtmEndSessionMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        _sessions.Remove((ent.Owner, user));
        SendUnauthorized(ent, user, BankTransactionStatus.NotAuthorized);
    }

    private void OnTransaction(Entity<BankAtmComponent> ent, ref BankAtmTransactionMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (!_sessions.Contains((ent.Owner, user)) || !TryGetCardAccount(ent, user, out var account))
        {
            _sessions.Remove((ent.Owner, user));
            _audio.PlayPvs(ErrorSound, ent.Owner, AudioParams.Default.WithVolume(-2f));
            SendUnauthorized(ent, user, BankTransactionStatus.NotAuthorized);
            return;
        }

        var status = _transactions.Process(user, args.Request, out var state);
        if (status == BankTransactionStatus.Success)
        {
            _audio.PlayPvs(ConfirmSound, ent.Owner, AudioParams.Default.WithVolume(-6f));
            var sound = args.Request.Operation == BankOperation.Withdraw
                ? WithdrawSound
                : DepositSound;
            _audio.PlayPvs(sound, ent.Owner, AudioParams.Default.WithVolume(-2f));
        }
        else if (status != BankTransactionStatus.DuplicateRequest)
        {
            // Duplicate retries are deliberately silent: the first request
            // already produced its result and the retry must not repeat it.
            _audio.PlayPvs(ErrorSound, ent.Owner, AudioParams.Default.WithVolume(-2f));
        }

        _ui.SetUiState(ent.Owner, BankAtmUiKey.Key,
            state ?? _transactions.BuildState(account, status, args.Request, authorized: true,
                cardOwnerName: account.Comp.OwnerName, cash: _transactions.GetHeldCash(user)));
    }

    private void OnCardInsertAttempt(Entity<BankAtmComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Slot != ent.Comp.CardSlot
            || !TryComp<IdCardComponent>(args.Item, out var idCard)
            || !string.IsNullOrWhiteSpace(idCard.BankAccountId)
            || args.User is not { } user
            || !_bank.TryGetPlayerAccount(user, out _, out var own))
        {
            return;
        }

        // Keep the convenience of attaching a blank replacement card to the
        // acting character's account. This runs inside the standard ItemSlots
        // insert attempt, so insertion and its sound have a single owner.
        idCard.BankAccountId = own.AccountId;
        Dirty(args.Item, idCard);
    }

    private void OnCardInserted(Entity<BankAtmComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != BankAtmComponent.CardSlotId)
            return;

        Log.Debug($"Bank ATM {ent.Owner}: card inserted {ToPrettyString(args.Entity)}; " +
                  $"parent={Transform(args.Entity).ParentUid}.");

        ClearSessions(ent.Owner);

        if (_openUsers.TryGetValue(ent.Owner, out var user))
            SendUnauthorized(ent, user, BankTransactionStatus.InvalidRequest);
    }

    private void OnCardRemoved(Entity<BankAtmComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != BankAtmComponent.CardSlotId)
            return;

        Log.Debug($"Bank ATM {ent.Owner}: card removed {ToPrettyString(args.Entity)}; " +
                  $"slot after={ent.Comp.CardSlot.Item?.ToString() ?? "empty"}, " +
                  $"parent after={Transform(args.Entity).ParentUid}.");

        ClearSessions(ent.Owner);

        if (_openUsers.TryGetValue(ent.Owner, out var user))
            SendUnauthorized(ent, user, BankTransactionStatus.NoCard);
    }

    private void ClearSessions(EntityUid atm)
    {
        _sessions.RemoveWhere(session => session.Atm == atm);
    }

    private bool TryGetCardAccount(Entity<BankAtmComponent> ent, EntityUid user, out Entity<EconomyAccountComponent> account)
    {
        account = default;

        if (ent.Comp.CardSlot.Item is not { } card ||
            !TryComp<IdCardComponent>(card, out var idCard) ||
            string.IsNullOrWhiteSpace(idCard.BankAccountId))
        {
            return false;
        }

        if (!_bank.TryGetPlayerAccount(user, out _, out var own) ||
            own.AccountId != idCard.BankAccountId)
        {
            return false;
        }

        return _bank.TryFindAccountById(idCard.BankAccountId, out account);
    }

    private void SendUnauthorized(Entity<BankAtmComponent> ent, EntityUid user, BankTransactionStatus status)
    {
        var card = ent.Comp.CardSlot.Item;
        var cardOwner = string.Empty;
        if (card is { } cardUid && TryComp<IdCardComponent>(cardUid, out var idCard))
            cardOwner = idCard.FullName ?? string.Empty;

        var state = new BankAccountUiState(
            null,
            string.Empty,
            0,
            null,
            status,
            hasCard: card != null,
            authorized: false,
            cardOwnerName: cardOwner);

        _ui.SetUiState(ent.Owner, BankAtmUiKey.Key, state);
    }
}
