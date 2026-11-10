using Content.Server._VanGuard.Economy.Components;
using Content.Server.Station.Systems;
using Content.Shared._VanGuard.Economy;
using Content.Shared.Access.Systems;
using Content.Shared.CartridgeLoader;
using Content.Shared.Ghost.Components;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.PDA;
using Content.Shared.Roles.Jobs;
using Content.Shared.Station.Components;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Economy.Systems;

public sealed partial class BankCartridgeSystem : EntitySystem
{
    private const int MaxSearchQueryLength = 64;
    private const int MaxSearchResults = 20;

    private static readonly TimeSpan SearchCooldown = TimeSpan.FromMilliseconds(250);

    [Dependency] private EconomyBankSystem _bank = default!;
    [Dependency] private EconomyBankTransactionSystem _transactions = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private ServerStationSystem _station = default!;
    [Dependency] private CartridgeLoaderSystem _loader = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BankCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<BankCartridgeComponent, CartridgeAddedEvent>(OnCartridgeAdded);
        SubscribeLocalEvent<BankCartridgeComponent, CartridgeRemovedEvent>(OnCartridgeRemoved);
        SubscribeLocalEvent<BankCartridgeComponent, CartridgeMessageEvent>(OnMessage);
        SubscribeLocalEvent<BankAccountChangedEvent>(OnAccountChanged);
    }

    private void OnUiReady(Entity<BankCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        // Bind the view to the player who actually opened the PDA. A stolen PDA
        // therefore shows the holder's own account (or no account), never the
        // original owner's balance.
        ent.Comp.LoaderUid = args.Loader.Owner;
        ent.Comp.ViewerUid = args.Actor;
        ResetTransferState(ent.Comp);
        SendState(ent, args.Actor);
    }

    private void OnCartridgeAdded(Entity<BankCartridgeComponent> ent, ref CartridgeAddedEvent args)
    {
        ent.Comp.LoaderUid = args.Loader;
    }

    private void OnCartridgeRemoved(Entity<BankCartridgeComponent> ent, ref CartridgeRemovedEvent args)
    {
        ent.Comp.LoaderUid = null;
        ent.Comp.ViewerUid = null;
        ResetTransferState(ent.Comp);
    }

    private void OnMessage(Entity<BankCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args.Actor is not { Valid: true } actor || ent.Comp.ViewerUid != actor)
            return;

        switch (args)
        {
            case BankCartridgeSearchMessage search:
                HandleSearch(ent, actor, search.Query);
                break;
            case BankCartridgeTransferMessage transfer:
                HandleTransfer(ent, actor, transfer);
                break;
            default:
                return;
        }

        SendState(ent, actor);
    }

    private void HandleSearch(Entity<BankCartridgeComponent> ent, EntityUid actor, string query)
    {
        query = (query ?? string.Empty).Trim();
        var now = _timing.CurTime;
        if (now < ent.Comp.NextSearchTime)
        {
            ent.Comp.TransferSearchStatus = BankTransferSearchStatus.RateLimited;
            ent.Comp.TransferQuery = query;
            ent.Comp.TransferResults.Clear();
            return;
        }

        ent.Comp.NextSearchTime = now + SearchCooldown;
        var result = SearchRecipients(actor, query);
        ent.Comp.TransferSearchStatus = result.Status;
        ent.Comp.TransferQuery = result.Query;
        ent.Comp.TransferResults = result.Recipients;
    }

    private void HandleTransfer(
        Entity<BankCartridgeComponent> ent,
        EntityUid actor,
        BankCartridgeTransferMessage message)
    {
        var status = _transactions.Transfer(
            actor,
            message.Recipient,
            message.Amount,
            message.RequestId,
            out var result);

        ent.Comp.LastTransferStatus = status;
        ent.Comp.LastTransferRequestId = message.RequestId;
        ent.Comp.LastTransferAmount = message.Amount;

        if (status == BankTransferStatus.Success && result.RecipientBody is { } recipientBody)
            NotifyRecipient(recipientBody, result.SenderName, result.Amount);
    }

    /// <summary>
    ///     Searches only characters that are currently on the sender's station
    ///     and have a valid personal account. No balances, PINs, full account
    ///     numbers or account ids are returned.
    /// </summary>
    public BankRecipientSearchResult SearchRecipients(EntityUid actor, string query)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length > MaxSearchQueryLength)
            return new BankRecipientSearchResult(BankTransferSearchStatus.InvalidQuery, query, new());

        if (query.Length == 0)
            return new BankRecipientSearchResult(BankTransferSearchStatus.EmptyQuery, query, new());

        if (!_bank.TryGetPlayerAccount(actor, out var senderMindUid, out var senderAccount)
            || !EconomyBankSystem.IsValidAccountId(senderAccount.AccountId))
        {
            return new BankRecipientSearchResult(BankTransferSearchStatus.NoAccount, query, new());
        }

        if (!TryComp<MindComponent>(senderMindUid, out var senderMind)
            || senderMind.OwnedEntity is not { } senderBody
            || !Exists(senderBody))
        {
            return new BankRecipientSearchResult(BankTransferSearchStatus.NoAccount, query, new());
        }

        var station = GetStation(senderBody);
        if (station == null)
        {
            Log.Info($"Bank search '{query}' by {ToPrettyString(actor)}: sender body has no station.");
            return new BankRecipientSearchResult(BankTransferSearchStatus.NoAccount, query, new());
        }

        var results = new List<BankTransferRecipient>();
        var seenAccounts = new HashSet<string>(StringComparer.Ordinal);
        var scanned = 0;
        var skippedSender = 0;
        var skippedInvalid = 0;
        var skippedNpc = 0;
        var skippedBody = 0;
        var skippedStation = 0;
        var skippedName = 0;
        var skippedDuplicate = 0;

        var accountQuery = EntityQueryEnumerator<EconomyAccountComponent, MindComponent>();
        while (accountQuery.MoveNext(out var mindUid, out var account, out var mind))
        {
            scanned++;

            if (mindUid == senderMindUid)
            {
                skippedSender++;
                continue;
            }

            if (!EconomyBankSystem.IsValidAccountId(account.AccountId)
                || string.Equals(account.AccountId, senderAccount.AccountId, StringComparison.Ordinal))
            {
                skippedInvalid++;
                continue;
            }

            // Player-origin minds remain searchable even if their current
            // UserId is temporarily absent (SSD/admin possession). NPC/service
            // minds created without a user have no OriginalOwnerUserId.
            if (mind.UserId == null && mind.OriginalOwnerUserId == null)
            {
                skippedNpc++;
                continue;
            }

            if (mind.OwnedEntity is not { } body
                || body == senderBody
                || !Exists(body)
                || !TryComp<MindContainerComponent>(body, out var container)
                || container.Mind != mindUid)
            {
                skippedBody++;
                continue;
            }

            if (HasComp<GhostComponent>(body))
            {
                skippedNpc++;
                continue;
            }

            if (GetStation(body) != station)
            {
                skippedStation++;
                continue;
            }

            var name = !string.IsNullOrWhiteSpace(mind.CharacterName)
                ? mind.CharacterName!
                : account.OwnerName;
            if (string.IsNullOrWhiteSpace(name))
                name = Name(body);
            if (string.IsNullOrWhiteSpace(name)
                || !name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                skippedName++;
                continue;
            }

            if (!seenAccounts.Add(account.AccountId))
            {
                skippedDuplicate++;
                continue;
            }

            string? jobTitle = null;
            if (_jobs.MindTryGetJob(mindUid, out var job))
            {
                jobTitle = job.LocalizedName;
            }
            else if (_idCard.TryFindIdCard(body, out var idCard)
                && !string.IsNullOrWhiteSpace(idCard.Comp.LocalizedJobTitle))
            {
                jobTitle = idCard.Comp.LocalizedJobTitle;
            }

            results.Add(new BankTransferRecipient(
                GetNetEntity(mindUid),
                name,
                jobTitle,
                BankAccountFormat.Mask(account.AccountId)));
        }

        results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        if (results.Count > MaxSearchResults)
            results.RemoveRange(MaxSearchResults, results.Count - MaxSearchResults);

        // One line per search is enough to diagnose a real-game mismatch without
        // spamming the log for every candidate or every frame. Successful
        // searches stay at debug level; an empty result is the case worth
        // surfacing.
        var summary = $"Bank search '{query}' by {ToPrettyString(actor)}: station={station}, scanned={scanned}, "
                      + $"sender={skippedSender}, invalidAccount={skippedInvalid}, npc={skippedNpc}, "
                      + $"body={skippedBody}, stationMismatch={skippedStation}, name={skippedName}, "
                      + $"duplicate={skippedDuplicate}, results={results.Count}.";

        if (results.Count == 0)
            Log.Info(summary);
        else
            Log.Debug(summary);

        return new BankRecipientSearchResult(
            results.Count == 0 ? BankTransferSearchStatus.NoResults : BankTransferSearchStatus.Results,
            query,
            results);
    }

    /// <summary>
    /// Resolves the station from the body. A StationTracker can deliberately
    /// tether an entity to a station without a grid, but a player body with an
    /// unset tracker must still fall back to its current station grid.
    /// </summary>
    private EntityUid? GetStation(EntityUid body)
    {
        if (_station.GetOwningStation(body) is { } station)
            return station;

        var xform = Transform(body);
        if (xform.GridUid is not { } grid)
            return null;

        if (TryComp<StationMemberComponent>(grid, out var member))
            return member.Station;

        // Some admin-loaded or remapped grids can have station data without
        // the usual StationMember component. Keep the same station boundary;
        // never fall back to "any grid".
        foreach (var stationEntity in _station.GetStations())
        {
            var stationCandidate = new Entity<StationDataComponent?>(stationEntity.Owner, stationEntity.Comp);
            if (_station.GetLargestGrid(stationCandidate) == grid)
                return stationEntity.Owner;
        }

        return null;
    }

    private void NotifyRecipient(EntityUid recipientBody, string? senderName, int amount)
    {
        if (!_idCard.TryFindIdCard(recipientBody, out var idCard))
            return;

        var cardUid = idCard.Owner;
        var pdaQuery = EntityQueryEnumerator<PdaComponent, CartridgeLoaderComponent>();
        while (pdaQuery.MoveNext(out var pdaUid, out var pda, out var loader))
        {
            if (pda.ContainedId != cardUid && pda.PdaOwner != cardUid)
                continue;

            var sender = string.IsNullOrWhiteSpace(senderName)
                ? Loc.GetString("generic-unknown-title")
                : senderName;

            _loader.SendNotification(
                pdaUid,
                Loc.GetString("bank-transfer-notification-title"),
                Loc.GetString("bank-transfer-notification-body",
                    ("amount", amount.ToString()),
                    ("sender", sender)),
                loader);
            return;
        }
    }

    private void OnAccountChanged(ref BankAccountChangedEvent args)
    {
        var query = EntityQueryEnumerator<BankCartridgeComponent>();
        while (query.MoveNext(out var cartridgeUid, out var component))
        {
            if (component.LoaderUid is not { } loader || component.ViewerUid is not { } viewer)
                continue;

            var account = FindAccountForViewer(viewer);
            if (account is not { } found || found.Owner != args.AccountOwner)
                continue;

            SendState((cartridgeUid, component), viewer, loader);
        }
    }

    private void SendState(Entity<BankCartridgeComponent> ent, EntityUid viewer, EntityUid? loader = null)
    {
        loader ??= ent.Comp.LoaderUid;
        if (loader is not { } loaderUid)
            return;

        var account = FindAccountForViewer(viewer);
        var accountState = account is { } found ? _transactions.BuildState(found) : EmptyState();
        var transferState = new BankTransferUiState(
            ent.Comp.TransferSearchStatus,
            ent.Comp.TransferQuery,
            ent.Comp.TransferResults,
            ent.Comp.LastTransferStatus,
            ent.Comp.LastTransferRequestId,
            ent.Comp.LastTransferAmount);

        _loader.UpdateCartridgeUiState(loaderUid, new BankCartridgeUiState(accountState, transferState));
    }

    private Entity<EconomyAccountComponent>? FindAccountForViewer(EntityUid viewer)
    {
        if (_bank.TryGetPlayerAccount(viewer, out var mindUid, out var account))
            return (mindUid, account);

        return null;
    }

    private static void ResetTransferState(BankCartridgeComponent component)
    {
        component.TransferQuery = string.Empty;
        component.TransferResults.Clear();
        component.TransferSearchStatus = BankTransferSearchStatus.Idle;
        component.LastTransferStatus = BankTransferStatus.None;
        component.LastTransferRequestId = 0;
        component.LastTransferAmount = 0;
        component.NextSearchTime = default;
    }

    private static BankAccountUiState EmptyState(
        BankTransactionStatus status = BankTransactionStatus.NoAccount)
    {
        return new BankAccountUiState(null, string.Empty, 0, null, status);
    }
}

public readonly record struct BankRecipientSearchResult(
    BankTransferSearchStatus Status,
    string Query,
    List<BankTransferRecipient> Recipients);
