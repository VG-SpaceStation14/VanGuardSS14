#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._VanGuard.Economy.Components;
using Content.Server._VanGuard.Economy.Systems;
using Content.Server.Station.Systems;
using Content.Shared._VanGuard.Economy;
using Content.Shared.CartridgeLoader;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._VanGuard.Economy;

[TestFixture]
public sealed class BankTransferTest : InteractionTest
{
    private EntityUid _station = default;

    private async Task<ICommonSession[]> SetupStation(int sessions)
    {
        var result = await Server.AddDummySessions(sessions);
        await Server.WaitPost(() =>
        {
            _station = SEntMan.SpawnEntity(null, new EntityCoordinates(MapData.MapUid, default));
            SEntMan.AddComponent<StationDataComponent>(_station);
            SEntMan.System<ServerStationSystem>().AddGridToStation(_station, MapData.Grid);
        });
        await RunTicks(2);
        return result;
    }

    private async Task<(EntityUid Mind, EntityUid Body)> SpawnCharacter(
        ICommonSession? session,
        string name,
        int index,
        bool withAccount = true,
        bool onStation = true)
    {
        EntityUid mindUid = default;
        EntityUid body = default;
        await Server.WaitPost(() =>
        {
            var onStationCoords = new Vector2(
                0.1f + index % 5 * 0.15f,
                0.1f + index / 5 % 5 * 0.15f);
            var coords = onStation
                ? new EntityCoordinates(MapData.Grid, onStationCoords)
                : new EntityCoordinates(MapData.MapUid, new Vector2(index + 0.5f, 0.5f));

            body = SEntMan.SpawnEntity("InteractionTestMob", coords);
            SEntMan.EnsureComponent<MindContainerComponent>(body);

            var minds = SEntMan.System<SharedMindSystem>();
            var mind = minds.CreateMind(session?.UserId);
            minds.TransferTo(mind, body, mind: mind);
            mind.Comp.CharacterName = name;

            if (withAccount)
            {
                var bank = SEntMan.System<EconomyBankSystem>();
                var account = bank.EnsureAccount(mind.Owner, mind.Comp);
                account.OwnerName = name;
            }

            mindUid = mind.Owner;
        });
        await RunTicks(1);
        return (mindUid, body);
    }

    private async Task<(EntityUid Mind, EntityUid Body)> SpawnRealCharacter(
        ICommonSession session,
        string name,
        int index)
    {
        EntityUid mindUid = default;
        EntityUid body = default;
        await Server.WaitPost(() =>
        {
            var coords = new EntityCoordinates(MapData.Grid,
                new Vector2(0.1f + index % 5 * 0.15f, 0.1f + index / 5 % 5 * 0.15f));
            body = SEntMan.SpawnEntity("MobHuman", coords);
            SEntMan.EnsureComponent<MindContainerComponent>(body);
            SEntMan.EnsureComponent<HumanoidProfileComponent>(body);

            var minds = SEntMan.System<SharedMindSystem>();
            var mind = minds.CreateMind(session.UserId);
            mind.Comp.CharacterName = name;
            minds.TransferTo(mind, body, mind: mind);
            mindUid = mind.Owner;
        });
        await RunTicks(3);
        return (mindUid, body);
    }

    private async Task<((EntityUid Mind, EntityUid Body) Sender, (EntityUid Mind, EntityUid Body) Recipient)>
        SetupTransferPair(int senderBalance, int recipientBalance)
    {
        var sessions = await SetupStation(2);
        var sender = await SpawnCharacter(sessions[0], "Alice", 0);
        var recipient = await SpawnCharacter(sessions[1], "Bob", 1);

        await Server.WaitPost(() =>
        {
            var bank = SEntMan.System<EconomyBankSystem>();
            if (senderBalance > 0)
                bank.Deposit((sender.Mind, SEntMan.GetComponent<EconomyAccountComponent>(sender.Mind)), senderBalance, "test-funding");
            if (recipientBalance > 0)
                bank.Deposit((recipient.Mind, SEntMan.GetComponent<EconomyAccountComponent>(recipient.Mind)), recipientBalance, "test-funding");
        });
        await RunTicks(1);
        return (sender, recipient);
    }

    [Test]
    public async Task SearchFindsOnlyStationCharactersWithAccounts()
    {
        var sessions = await SetupStation(6);
        var sender = await SpawnCharacter(sessions[0], "Alice", 0);
        var bob = await SpawnCharacter(sessions[1], "Bob", 1);
        var robert = await SpawnCharacter(sessions[2], "Robert", 2);
        await SpawnCharacter(sessions[3], "Charlie", 3, onStation: false);
        await SpawnCharacter(sessions[4], "Dave", 4, withAccount: false);
        await SpawnCharacter(null, "Service", 5);
        var invalid = await SpawnCharacter(sessions[5], "Invalid", 6);
        await Server.WaitPost(() => SEntMan.DeleteEntity(invalid.Body));
        await RunTicks(2);

        var system = SEntMan.System<BankCartridgeSystem>();
        var bobAccount = SEntMan.GetComponent<EconomyAccountComponent>(bob.Mind);

        await Server.WaitPost(() =>
        {
            var result = system.SearchRecipients(sender.Body, "Bob");
            Assert.That(result.Status, Is.EqualTo(BankTransferSearchStatus.Results));
            Assert.That(result.Recipients, Has.Count.EqualTo(1));
            Assert.That(result.Recipients[0].Name, Is.EqualTo("Bob"));
            Assert.That(result.Recipients[0].MaskedAccount,
                Is.EqualTo(BankAccountFormat.Mask(bobAccount.AccountId)));
            Assert.That(result.Recipients[0].MaskedAccount, Is.Not.EqualTo(bobAccount.AccountId),
                "the search must never expose the full account number");

            Assert.That(system.SearchRecipients(sender.Body, "Alice").Status,
                Is.EqualTo(BankTransferSearchStatus.NoResults), "the sender must be excluded");
            Assert.That(system.SearchRecipients(sender.Body, "Charlie").Status,
                Is.EqualTo(BankTransferSearchStatus.NoResults), "off-station characters must be excluded");
            Assert.That(system.SearchRecipients(sender.Body, "Dave").Status,
                Is.EqualTo(BankTransferSearchStatus.NoResults), "characters without an account must be excluded");
            Assert.That(system.SearchRecipients(sender.Body, "Service").Status,
                Is.EqualTo(BankTransferSearchStatus.NoResults), "NPC/service minds must be excluded");
            Assert.That(system.SearchRecipients(sender.Body, "Invalid").Status,
                Is.EqualTo(BankTransferSearchStatus.NoResults), "invalid/deleted bodies must be excluded");
            Assert.That(system.SearchRecipients(sender.Body, new string('a', 65)).Status,
                Is.EqualTo(BankTransferSearchStatus.InvalidQuery));
            Assert.That(system.SearchRecipients(sender.Body, "").Status,
                Is.EqualTo(BankTransferSearchStatus.EmptyQuery));

            // Duplicate accounts must not produce duplicate rows.
            var robertAccount = SEntMan.GetComponent<EconomyAccountComponent>(robert.Mind);
            robertAccount.AccountId = bobAccount.AccountId;
            var duplicate = system.SearchRecipients(sender.Body, "o");
            Assert.That(duplicate.Recipients.Select(recipient => recipient.Name), Is.Unique);
            Assert.That(duplicate.Recipients, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task CartridgeLoaderRelaysSearchMessageWithActor()
    {
        var sessions = await SetupStation(2);
        var sender = await SpawnRealCharacter(sessions[0], "Alice", 0);
        await SpawnRealCharacter(sessions[1], "Bob", 1);

        EntityUid pda = default;
        EntityUid cartridge = default;
        await Server.WaitPost(() =>
        {
            var coords = new EntityCoordinates(MapData.Grid, new Vector2(0.3f, 0.3f));
            pda = SEntMan.SpawnEntity("PassengerPDA", coords);
            var loader = SEntMan.GetComponent<CartridgeLoaderComponent>(pda);
            var loaderSystem = SEntMan.System<CartridgeLoaderSystem>();
            cartridge = loaderSystem.GetAllPrograms(pda)
                .Single(uid => SEntMan.HasComponent<BankCartridgeComponent>(uid));
            loaderSystem.ActivateProgram((pda, loader), cartridge);

            var ready = new CartridgeLoaderUiMessage(
                SEntMan.GetNetEntity(cartridge),
                CartridgeUiMessageAction.UIReady)
            {
                Actor = sender.Body,
            };
            SEntMan.EventBus.RaiseLocalEvent(pda, ready);

            var search = new CartridgeUiMessage(new BankCartridgeSearchMessage("Bob"))
            {
                Actor = sender.Body,
            };
            SEntMan.EventBus.RaiseLocalEvent(pda, search);

            var component = SEntMan.GetComponent<BankCartridgeComponent>(cartridge);
            Assert.That(component.ViewerUid, Is.EqualTo(sender.Body),
                "the loader UI-ready event must bind the real player entity");
            Assert.That(component.TransferSearchStatus, Is.EqualTo(BankTransferSearchStatus.Results));
            Assert.That(component.TransferResults, Has.Count.EqualTo(1));
            Assert.That(component.TransferResults[0].Name, Is.EqualTo("Bob"));
        });
    }

    [Test]
    public async Task SearchFallsBackToTheStationGridWhenTrackerHasNoStation()
    {
        var (sender, recipient) = await SetupTransferPair(100, 0);
        var system = SEntMan.System<BankCartridgeSystem>();
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();

        await Server.WaitPost(() =>
        {
            // A StationTracker with no station must not hide a body that is
            // standing on a station grid. This is the shape a stale tracker
            // takes on a real character.
            SEntMan.EnsureComponent<StationTrackerComponent>(sender.Body);
            SEntMan.EnsureComponent<StationTrackerComponent>(recipient.Body);
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            var result = system.SearchRecipients(sender.Body, "Bob");
            Assert.That(result.Status, Is.EqualTo(BankTransferSearchStatus.Results));
            Assert.That(result.Recipients, Has.Count.EqualTo(1));
            Assert.That(result.Recipients[0].Name, Is.EqualTo("Bob"));

            Assert.That(transactions.Transfer(sender.Body, SEntMan.GetNetEntity(recipient.Mind),
                10, 1001, out _), Is.EqualTo(BankTransferStatus.Success));
        });
    }

    [Test]
    public async Task SearchFallsBackToStationDataWhenGridHasNoStationMember()
    {
        var (sender, recipient) = await SetupTransferPair(100, 0);
        var system = SEntMan.System<BankCartridgeSystem>();
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();

        await Server.WaitPost(() =>
        {
            // Admin-loaded/remapped station grids can retain StationData.Grids
            // without a StationMember component on the grid.
            SEntMan.RemoveComponent<StationMemberComponent>(MapData.Grid);
            var stationData = SEntMan.GetComponent<StationDataComponent>(_station);
            var grids = (HashSet<EntityUid>) typeof(StationDataComponent)
                .GetField("Grids", BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(stationData)!;
            grids.Add(MapData.Grid);
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            var result = system.SearchRecipients(sender.Body, "Bob");
            Assert.That(result.Status, Is.EqualTo(BankTransferSearchStatus.Results));
            Assert.That(result.Recipients, Has.Count.EqualTo(1));
            Assert.That(result.Recipients[0].Name, Is.EqualTo("Bob"));

            Assert.That(transactions.Transfer(sender.Body, SEntMan.GetNetEntity(recipient.Mind),
                10, 1002, out _), Is.EqualTo(BankTransferStatus.Success));
        });
    }

    [Test]
    public async Task SearchAndTransferAllowPlayerOriginMindWithoutActiveUser()
    {
        var (sender, recipient) = await SetupTransferPair(100, 0);
        var system = SEntMan.System<BankCartridgeSystem>();
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();
        var minds = SEntMan.System<SharedMindSystem>();

        await Server.WaitPost(() =>
        {
            var mind = SEntMan.GetComponent<MindComponent>(recipient.Mind);
            Assert.That(mind.UserId, Is.Not.Null);
            minds.SetUserId(recipient.Mind, null, mind);
            Assert.That(mind.UserId, Is.Null,
                "the test must simulate a player-origin mind without an active user id");
            Assert.That(mind.OriginalOwnerUserId, Is.Not.Null);
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            var result = system.SearchRecipients(sender.Body, "Bob");
            Assert.That(result.Status, Is.EqualTo(BankTransferSearchStatus.Results),
                "a player-origin character with a valid account and station must remain searchable");
            Assert.That(result.Recipients, Has.Count.EqualTo(1));
            Assert.That(result.Recipients[0].Name, Is.EqualTo("Bob"));

            Assert.That(transactions.Transfer(sender.Body, SEntMan.GetNetEntity(recipient.Mind),
                10, 1003, out _), Is.EqualTo(BankTransferStatus.Success));
        });
    }

    [Test]
    public async Task CartridgeSearchMessageUpdatesServerState()
    {
        var sessions = await SetupStation(2);
        var sender = await SpawnRealCharacter(sessions[0], "Alice", 0);
        await SpawnRealCharacter(sessions[1], "Bob", 1);

        EntityUid pda = default;
        EntityUid cartridge = default;
        await Server.WaitPost(() =>
        {
            var coords = new EntityCoordinates(MapData.Grid, new Vector2(0.3f, 0.3f));
            pda = SEntMan.SpawnEntity("PassengerPDA", coords);
            cartridge = SEntMan.SpawnEntity("BankCartridge", coords);
            var loader = SEntMan.GetComponent<CartridgeLoaderComponent>(pda);
            var ready = new CartridgeUiReadyEvent((pda, loader), sender.Body);
            SEntMan.EventBus.RaiseLocalEvent(cartridge, ref ready);

            CartridgeMessageEvent message = new BankCartridgeSearchMessage("Bob")
            {
                Actor = sender.Body,
                LoaderUid = SEntMan.GetNetEntity(pda),
            };
            SEntMan.EventBus.RaiseLocalEvent(cartridge, message);

            var component = SEntMan.GetComponent<BankCartridgeComponent>(cartridge);
            Assert.That(component.ViewerUid, Is.EqualTo(sender.Body));
            Assert.That(component.TransferSearchStatus, Is.EqualTo(BankTransferSearchStatus.Results));
            Assert.That(component.TransferQuery, Is.EqualTo("Bob"));
            Assert.That(component.TransferResults, Has.Count.EqualTo(1));
            Assert.That(component.TransferResults[0].Name, Is.EqualTo("Bob"));
        });
    }

    [Test]
    public async Task SearchFindsRealHumanoidCharactersWithWalletAccounts()
    {
        var sessions = await SetupStation(2);
        var sender = await SpawnRealCharacter(sessions[0], "Alice", 0);
        var recipient = await SpawnRealCharacter(sessions[1], "Bob", 1);
        var system = SEntMan.System<BankCartridgeSystem>();
        var bank = SEntMan.System<EconomyBankSystem>();

        await Server.WaitPost(() =>
        {
            Assert.That(bank.TryGetPlayerAccount(sender.Body, out _, out _), Is.True,
                "the wallet system must create a mind account for a real humanoid");
            Assert.That(bank.TryGetPlayerAccount(recipient.Body, out _, out _), Is.True);

            var toRecipient = system.SearchRecipients(sender.Body, "Bob");
            Assert.That(toRecipient.Status, Is.EqualTo(BankTransferSearchStatus.Results));
            Assert.That(toRecipient.Recipients.Select(recipient => recipient.Name),
                Is.EquivalentTo(new[] { "Bob" }));

            var toSender = system.SearchRecipients(recipient.Body, "Alice");
            Assert.That(toSender.Status, Is.EqualTo(BankTransferSearchStatus.Results));
            Assert.That(toSender.Recipients.Select(recipient => recipient.Name),
                Is.EquivalentTo(new[] { "Alice" }));

            Assert.That(system.SearchRecipients(sender.Body, "Alice").Status,
                Is.EqualTo(BankTransferSearchStatus.NoResults));
        });
    }

    [Test]
    public async Task SearchRespectsResultLimit()
    {
        var sessions = await SetupStation(23);
        var sender = await SpawnCharacter(sessions[0], "Sender", 0);
        for (var i = 1; i < sessions.Length; i++)
            await SpawnCharacter(sessions[i], $"Test{i:00}", i);

        var system = SEntMan.System<BankCartridgeSystem>();
        await Server.WaitPost(() =>
        {
            var result = system.SearchRecipients(sender.Body, "Test");
            Assert.That(result.Status, Is.EqualTo(BankTransferSearchStatus.Results));
            Assert.That(result.Recipients, Has.Count.EqualTo(20));
        });
    }

    [Test]
    public async Task TransferMovesMoneyAndWritesLinkedHistory()
    {
        var (sender, recipient) = await SetupTransferPair(1000, 100);
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();

        await Server.WaitPost(() =>
        {
            var senderAccount = SEntMan.GetComponent<EconomyAccountComponent>(sender.Mind);
            var recipientAccount = SEntMan.GetComponent<EconomyAccountComponent>(recipient.Mind);
            var senderHistoryCount = senderAccount.History.Count;
            var recipientHistoryCount = recipientAccount.History.Count;

            var status = transactions.Transfer(
                sender.Body,
                SEntMan.GetNetEntity(recipient.Mind),
                250,
                501,
                out var result);

            Assert.That(status, Is.EqualTo(BankTransferStatus.Success));
            Assert.That(result.RecipientBody, Is.EqualTo(recipient.Body));
            Assert.That(senderAccount.Balance, Is.EqualTo(750));
            Assert.That(recipientAccount.Balance, Is.EqualTo(350));
            Assert.That(senderAccount.History, Has.Count.EqualTo(senderHistoryCount + 1));
            Assert.That(recipientAccount.History, Has.Count.EqualTo(recipientHistoryCount + 1));

            var outgoing = senderAccount.History.Last();
            Assert.That(outgoing.Reason, Is.EqualTo("bank-transfer-out"));
            Assert.That(outgoing.Delta, Is.EqualTo(-250));
            Assert.That(outgoing.CounterpartyName, Is.EqualTo("Bob"));
            Assert.That(outgoing.CounterpartyAccount,
                Is.EqualTo(BankAccountFormat.Mask(recipientAccount.AccountId)));
            Assert.That(outgoing.TransferId, Is.Not.Null);

            var incoming = recipientAccount.History.Last();
            Assert.That(incoming.Reason, Is.EqualTo("bank-transfer-in"));
            Assert.That(incoming.Delta, Is.EqualTo(250));
            Assert.That(incoming.CounterpartyName, Is.EqualTo("Alice"));
            Assert.That(incoming.CounterpartyAccount,
                Is.EqualTo(BankAccountFormat.Mask(senderAccount.AccountId)));
            Assert.That(incoming.TransferId, Is.EqualTo(outgoing.TransferId));

            var duplicate = transactions.Transfer(
                sender.Body,
                SEntMan.GetNetEntity(recipient.Mind),
                250,
                501,
                out _);

            Assert.That(duplicate, Is.EqualTo(BankTransferStatus.DuplicateRequest));
            Assert.That(senderAccount.Balance, Is.EqualTo(750));
            Assert.That(recipientAccount.Balance, Is.EqualTo(350));
            Assert.That(senderAccount.History, Has.Count.EqualTo(senderHistoryCount + 1));
            Assert.That(recipientAccount.History, Has.Count.EqualTo(recipientHistoryCount + 1));
        });
    }

    [Test]
    public async Task TransferRejectsInvalidRecipientsAndAmounts()
    {
        var sessions = await SetupStation(5);
        var sender = await SpawnCharacter(sessions[0], "Alice", 0);
        var recipient = await SpawnCharacter(sessions[1], "Bob", 1);
        await SpawnCharacter(sessions[2], "Charlie", 2, onStation: false);
        var noAccount = await SpawnCharacter(sessions[3], "Dave", 3, withAccount: false);
        var offStation = await SpawnCharacter(sessions[4], "Erin", 4, onStation: false);
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();

        await Server.WaitPost(() =>
        {
            var bank = SEntMan.System<EconomyBankSystem>();
            var senderAccount = SEntMan.GetComponent<EconomyAccountComponent>(sender.Mind);
            bank.Deposit((sender.Mind, senderAccount), 500, "test-funding");

            var recipientNet = SEntMan.GetNetEntity(recipient.Mind);
            Assert.That(transactions.Transfer(sender.Body, SEntMan.GetNetEntity(sender.Mind), 10, 601, out _),
                Is.EqualTo(BankTransferStatus.SelfTransfer));
            Assert.That(transactions.Transfer(sender.Body, new NetEntity(int.MaxValue), 10, 602, out _),
                Is.EqualTo(BankTransferStatus.RecipientNotFound));
            Assert.That(transactions.Transfer(sender.Body, SEntMan.GetNetEntity(noAccount.Mind), 10, 603, out _),
                Is.EqualTo(BankTransferStatus.RecipientNoAccount));
            Assert.That(transactions.Transfer(sender.Body, SEntMan.GetNetEntity(offStation.Mind), 10, 604, out _),
                Is.EqualTo(BankTransferStatus.RecipientNotOnStation));
            Assert.That(transactions.Transfer(sender.Body, recipientNet, 0, 605, out _),
                Is.EqualTo(BankTransferStatus.InvalidAmount));
            Assert.That(transactions.Transfer(sender.Body, recipientNet, -10, 606, out _),
                Is.EqualTo(BankTransferStatus.InvalidAmount));
            Assert.That(transactions.Transfer(sender.Body, recipientNet,
                    BankTransactionLimits.MaxTransaction + 1, 607, out _),
                Is.EqualTo(BankTransferStatus.InvalidAmount));
            Assert.That(transactions.Transfer(sender.Body, recipientNet, 501, 608, out _),
                Is.EqualTo(BankTransferStatus.InsufficientFunds));
            Assert.That(senderAccount.Balance, Is.EqualTo(500));
        });
    }

    [Test]
    public async Task TransferDoesNotPartiallyApplyOnOverflow()
    {
        var (sender, recipient) = await SetupTransferPair(100, 0);
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();

        await Server.WaitPost(() =>
        {
            var senderAccount = SEntMan.GetComponent<EconomyAccountComponent>(sender.Mind);
            var recipientAccount = SEntMan.GetComponent<EconomyAccountComponent>(recipient.Mind);
            recipientAccount.Balance = int.MaxValue - 5;
            var senderHistoryCount = senderAccount.History.Count;
            var recipientHistoryCount = recipientAccount.History.Count;

            var status = transactions.Transfer(
                sender.Body,
                SEntMan.GetNetEntity(recipient.Mind),
                10,
                701,
                out _);

            Assert.That(status, Is.EqualTo(BankTransferStatus.TransferFailed));
            Assert.That(senderAccount.Balance, Is.EqualTo(100));
            Assert.That(recipientAccount.Balance, Is.EqualTo(int.MaxValue - 5));
            Assert.That(senderAccount.History, Has.Count.EqualTo(senderHistoryCount));
            Assert.That(recipientAccount.History, Has.Count.EqualTo(recipientHistoryCount));
        });
    }

    [Test]
    public async Task ConcurrentTransfersCannotOverdraw()
    {
        var (sender, recipient) = await SetupTransferPair(100, 0);
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();

        await Server.WaitPost(() =>
        {
            var recipientNet = SEntMan.GetNetEntity(recipient.Mind);
            Assert.That(transactions.Transfer(sender.Body, recipientNet, 60, 801, out _),
                Is.EqualTo(BankTransferStatus.Success));
            Assert.That(transactions.Transfer(sender.Body, recipientNet, 60, 802, out _),
                Is.EqualTo(BankTransferStatus.InsufficientFunds));

            var senderAccount = SEntMan.GetComponent<EconomyAccountComponent>(sender.Mind);
            var recipientAccount = SEntMan.GetComponent<EconomyAccountComponent>(recipient.Mind);
            Assert.That(senderAccount.Balance, Is.EqualTo(40));
            Assert.That(recipientAccount.Balance, Is.EqualTo(60));
        });
    }
}
