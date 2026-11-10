#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._VanGuard.Economy.Components;
using Content.Server._VanGuard.Economy.Systems;
using Content.Shared._VanGuard.Economy;
using Content.Shared.Access.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Economy;

[TestFixture]
public sealed class BankEconomyTest : InteractionTest
{
    private EntityUid _mind;

    private async Task SetupAccount(int balance = 0)
    {
        var mindSystem = SEntMan.System<SharedMindSystem>();
        var bank = SEntMan.System<EconomyBankSystem>();
        await Server.WaitPost(() =>
        {
            var mind = mindSystem.CreateMind(ClientSession.UserId);
            _mind = mind.Owner;
            mindSystem.SetUserId(mind.Owner, ClientSession.UserId, mind.Comp);
            mindSystem.TransferTo(mind, SPlayer, mind: mind);
            var account = bank.EnsureAccount(_mind, mind.Comp);
            if (balance > 0)
                bank.Deposit((_mind, account), balance, "test-funding");
        });
        await RunTicks(2);
    }

    [Test]
    public async Task AccountIsSingleAndBoundToTheMind()
    {
        await SetupAccount();
        var bank = SEntMan.System<EconomyBankSystem>();

        Assert.That(bank.TryGetPlayerAccount(SPlayer, out var mindUid, out var account), Is.True);
        Assert.That(mindUid, Is.EqualTo(_mind));
        Assert.That(account.AccountId, Has.Length.EqualTo(12));
        Assert.That(account.AccountId.All(char.IsDigit), Is.True);
        Assert.That(bank.EnsureAccount(mindUid).AccountId, Is.EqualTo(account.AccountId),
            "repeated account lookup must not create a second balance");
        Assert.That(account.Pin, Has.Length.EqualTo(4));
        Assert.That(account.Pin.All(char.IsDigit), Is.True);
        Assert.That(SEntMan.GetComponent<MindComponent>(mindUid).MemoryEntries.Values,
            Does.Contain(account.Pin), "the PIN must be available through character memory");
    }

    [Test]
    public async Task OtherPlayerCannotResolveOwnAccountAsSomeoneElse()
    {
        await SetupAccount();
        var bank = SEntMan.System<EconomyBankSystem>();
        EntityUid otherMind = default;
        await Server.WaitPost(() =>
        {
            otherMind = SEntMan.System<SharedMindSystem>().CreateMind(null).Owner;
            bank.EnsureAccount(otherMind);
        });

        Assert.That(bank.TryGetPlayerAccount(SPlayer, out var ownMind, out var ownAccount), Is.True);
        Assert.That(ownMind, Is.EqualTo(_mind));
        Assert.That(ownAccount.Owner, Is.EqualTo(_mind));
        Assert.That(ownAccount.Owner, Is.Not.EqualTo(otherMind));
    }

    [Test]
    public async Task DepositConsumesCashAndWithdrawSpawnsCash()
    {
        await SetupAccount();
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();
        var hands = SEntMan.System<SharedHandsSystem>();
        var bank = SEntMan.System<EconomyBankSystem>();

        await Server.WaitPost(() =>
        {
            var cash = SEntMan.SpawnEntity("SpaceCash100", SEntMan.GetCoordinates(TargetCoords));
            hands.PickupOrDrop(SPlayer, cash);
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            var status = transactions.Process(SPlayer, new BankTransactionRequest(10, BankOperation.Deposit, 100), out var state);
            Assert.That(status, Is.EqualTo(BankTransactionStatus.Success));
            Assert.That(state?.Balance, Is.EqualTo(100));

            var withdraw = transactions.Process(SPlayer, new BankTransactionRequest(11, BankOperation.Withdraw, 40), out var withdrawState);
            Assert.That(withdraw, Is.EqualTo(BankTransactionStatus.Success));
            Assert.That(withdrawState?.Balance, Is.EqualTo(60));

            bank.TryGetPlayerAccount(SPlayer, out _, out var account);
            Assert.That(account.Balance, Is.EqualTo(60));
        });
        await RunTicks(2);

        var cashCount = 0;
        await Server.WaitPost(() =>
        {
            var lookup = SEntMan.System<EntityLookupSystem>();
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            foreach (var entity in lookup.GetEntitiesInRange(coords, 3f))
            {
                if (SEntMan.TryGetComponent<StackComponent>(entity, out var stack) && stack.StackTypeId == "Credit")
                    cashCount += stack.Count;
            }
        });
        Assert.That(cashCount, Is.EqualTo(40), "withdrawal must create exactly the requested cash");
    }

    [Test]
    public async Task DepositAllConsumesEveryHeldCashStack()
    {
        await SetupAccount();
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();
        var hands = SEntMan.System<SharedHandsSystem>();

        await Server.WaitPost(() =>
        {
            hands.AddHand(SPlayer, "second", HandLocation.Right);
            var first = SEntMan.SpawnEntity("SpaceCash100", SEntMan.GetCoordinates(TargetCoords));
            var second = SEntMan.SpawnEntity("SpaceCash100", SEntMan.GetCoordinates(TargetCoords));
            Assert.That(hands.TryPickupAnyHand(SPlayer, first), Is.True);
            Assert.That(hands.TryPickupAnyHand(SPlayer, second), Is.True);
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            Assert.That(transactions.GetHeldCash(SPlayer), Is.EqualTo(200));

            var status = transactions.Process(SPlayer,
                new BankTransactionRequest(201, BankOperation.DepositAll, 0), out var state);
            Assert.That(status, Is.EqualTo(BankTransactionStatus.Success));
            Assert.That(state?.Balance, Is.EqualTo(200));
            Assert.That(state?.LastAmount, Is.EqualTo(200));
            Assert.That(state?.Cash, Is.Zero);
            Assert.That(transactions.GetHeldCash(SPlayer), Is.Zero);
        });
    }

    [Test]
    public async Task DepositAllOverLimitDepositsTheLimitAndKeepsTheRest()
    {
        await SetupAccount();
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();
        var hands = SEntMan.System<SharedHandsSystem>();
        var stacks = SEntMan.System<SharedStackSystem>();

        await Server.WaitPost(() =>
        {
            var cash = SEntMan.SpawnEntity("SpaceCash", SEntMan.GetCoordinates(TargetCoords));
            stacks.SetCount(cash, BankTransactionLimits.MaxTransaction + 500);
            hands.PickupOrDrop(SPlayer, cash);
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            var status = transactions.Process(SPlayer,
                new BankTransactionRequest(202, BankOperation.DepositAll, 0), out var state);
            Assert.That(status, Is.EqualTo(BankTransactionStatus.Success));
            Assert.That(state?.Balance, Is.EqualTo(BankTransactionLimits.MaxTransaction));
            Assert.That(state?.LastAmount, Is.EqualTo(BankTransactionLimits.MaxTransaction));
            Assert.That(state?.Cash, Is.EqualTo(500));
            Assert.That(transactions.GetHeldCash(SPlayer), Is.EqualTo(500),
                "cash above the per-operation limit must remain in the player's hands");
        });
    }

    [Test]
    public async Task DepositAllWithoutCashIsRejected()
    {
        await SetupAccount();
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();

        await Server.WaitPost(() =>
        {
            var status = transactions.Process(SPlayer,
                new BankTransactionRequest(203, BankOperation.DepositAll, 0), out var state);
            Assert.That(status, Is.EqualTo(BankTransactionStatus.NoCash));
            Assert.That(state?.Balance, Is.Zero);
            Assert.That(state?.Cash, Is.Zero);
        });
    }

    [Test]
    public async Task DuplicateDepositAllDoesNotRepeat()
    {
        await SetupAccount();
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();
        var hands = SEntMan.System<SharedHandsSystem>();

        await Server.WaitPost(() =>
        {
            var cash = SEntMan.SpawnEntity("SpaceCash100", SEntMan.GetCoordinates(TargetCoords));
            hands.PickupOrDrop(SPlayer, cash);
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            var request = new BankTransactionRequest(204, BankOperation.DepositAll, 0);
            Assert.That(transactions.Process(SPlayer, request, out _),
                Is.EqualTo(BankTransactionStatus.Success));
            Assert.That(transactions.Process(SPlayer, request, out var duplicateState),
                Is.EqualTo(BankTransactionStatus.DuplicateRequest));
            Assert.That(duplicateState?.Balance, Is.EqualTo(100));
            Assert.That(duplicateState?.LastAmount, Is.EqualTo(100));
            Assert.That(transactions.GetHeldCash(SPlayer), Is.Zero);
        });
    }

    [Test]
    public async Task DepositRejectsMoreThanHeldCash()
    {
        await SetupAccount();
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();
        var hands = SEntMan.System<SharedHandsSystem>();

        await Server.WaitPost(() =>
        {
            var cash = SEntMan.SpawnEntity("SpaceCash100", SEntMan.GetCoordinates(TargetCoords));
            hands.PickupOrDrop(SPlayer, cash);
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            var status = transactions.Process(SPlayer,
                new BankTransactionRequest(205, BankOperation.Deposit, 101), out var state);
            Assert.That(status, Is.EqualTo(BankTransactionStatus.NoCash));
            Assert.That(state?.Balance, Is.Zero);
            Assert.That(transactions.GetHeldCash(SPlayer), Is.EqualTo(100),
                "a rejected deposit must not consume physical cash");
        });
    }

    [Test]
    public async Task WithdrawRejectsAmountAboveOperationLimit()
    {
        await SetupAccount(BankTransactionLimits.MaxTransaction + 100);
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();

        await Server.WaitPost(() =>
        {
            var status = transactions.Process(SPlayer,
                new BankTransactionRequest(206, BankOperation.Withdraw,
                    BankTransactionLimits.MaxTransaction + 1), out _);
            Assert.That(status, Is.EqualTo(BankTransactionStatus.InvalidAmount));

            var account = SEntMan.GetComponent<EconomyAccountComponent>(_mind);
            Assert.That(account.Balance, Is.EqualTo(BankTransactionLimits.MaxTransaction + 100));
        });
    }

    [Test]
    public async Task DuplicateRequestDoesNotRepeatTransaction()
    {
        await SetupAccount();
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();
        var hands = SEntMan.System<SharedHandsSystem>();

        await Server.WaitPost(() =>
        {
            var cash = SEntMan.SpawnEntity("SpaceCash100", SEntMan.GetCoordinates(TargetCoords));
            hands.PickupOrDrop(SPlayer, cash);
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            var request = new BankTransactionRequest(77, BankOperation.Deposit, 100);
            Assert.That(transactions.Process(SPlayer, request, out _), Is.EqualTo(BankTransactionStatus.Success));
            Assert.That(transactions.Process(SPlayer, request, out _), Is.EqualTo(BankTransactionStatus.DuplicateRequest));
            transactions.BuildState((_mind, SEntMan.GetComponent<EconomyAccountComponent>(_mind)));
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            var account = SEntMan.GetComponent<EconomyAccountComponent>(_mind);
            Assert.That(account.Balance, Is.EqualTo(100), "a retry must not deposit twice");
        });
    }

    [Test]
    public async Task WithdrawRejectsInsufficientFunds()
    {
        await SetupAccount();
        var transactions = SEntMan.System<EconomyBankTransactionSystem>();

        await Server.WaitPost(() =>
        {
            var status = transactions.Process(SPlayer, new BankTransactionRequest(90, BankOperation.Withdraw, 500), out _);
            Assert.That(status, Is.EqualTo(BankTransactionStatus.InsufficientFunds));
            var account = SEntMan.GetComponent<EconomyAccountComponent>(_mind);
            Assert.That(account.Balance, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task AtmCardPinAuthorizesAndWithdraws()
    {
        await SetupAccount(500);
        EntityUid atm = default;
        EntityUid card = default;
        EconomyAccountComponent account = default!;

        await Server.WaitPost(() =>
        {
            var bank = SEntMan.System<EconomyBankSystem>();
            bank.TryGetPlayerAccount(SPlayer, out _, out account);
            var coords = SEntMan.GetCoordinates(TargetCoords);
            atm = SEntMan.SpawnEntity("BankATM", coords);
            card = SEntMan.SpawnEntity("PassengerIDCard", coords);
            SEntMan.GetComponent<IdCardComponent>(card).BankAccountId = account.AccountId;

            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(atm, BankAtmComponent.CardSlotId, card, SPlayer), Is.True);
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            var auth = new BankAtmAuthMessage(account.Pin) { Actor = SPlayer };
            SEntMan.EventBus.RaiseLocalEvent(atm, auth);

            var request = new BankTransactionRequest(501, BankOperation.Withdraw, 100);
            var transaction = new BankAtmTransactionMessage(request) { Actor = SPlayer };
            SEntMan.EventBus.RaiseLocalEvent(atm, transaction);
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            var bank = SEntMan.System<EconomyBankSystem>();
            bank.TryGetPlayerAccount(SPlayer, out _, out var current);
            Assert.That(current.Balance, Is.EqualTo(400));
            Assert.That(current.History.Any(t => t.Reason == "bank-withdraw"), Is.True);
        });
    }

    [Test]
    public async Task AtmCardSurvivesRepeatedEjectAndAtmDeletion()
    {
        await SetupAccount(500);
        var slots = SEntMan.System<ItemSlotsSystem>();
        EntityUid atm = default;
        EntityUid card = default;
        EconomyAccountComponent account = default!;

        await Server.WaitPost(() =>
        {
            SEntMan.System<EconomyBankSystem>().TryGetPlayerAccount(SPlayer, out _, out account);
            var coords = SEntMan.GetCoordinates(TargetCoords);
            atm = SEntMan.SpawnEntity("BankATM", coords);
            card = SEntMan.SpawnEntity("PassengerIDCard", coords);
            Assert.That(slots.TryInsert(atm, BankAtmComponent.CardSlotId, card, SPlayer), Is.True);
            Assert.That(SEntMan.GetComponent<IdCardComponent>(card).BankAccountId,
                Is.EqualTo(account.AccountId),
                "a blank card must be attached through the standard insert-attempt hook");
        });
        await RunTicks(2);

        for (var cycle = 0; cycle < 4; cycle++)
        {
            await Server.WaitPost(() =>
            {
                Assert.That(slots.GetItemOrNull(atm, BankAtmComponent.CardSlotId), Is.EqualTo(card),
                    $"card must still be in the slot before eject cycle {cycle}");

                SEntMan.EventBus.RaiseLocalEvent(atm, new BankAtmEjectMessage { Actor = SPlayer });
                Assert.That(slots.GetItemOrNull(atm, BankAtmComponent.CardSlotId), Is.Null);
                Assert.That(SEntMan.EntityExists(card), Is.True,
                    $"card must survive eject cycle {cycle}");
                Assert.That(SEntMan.GetComponent<TransformComponent>(card).ParentUid,
                    Is.Not.EqualTo(EntityUid.Invalid), "ejected card must have a parent");

                Assert.That(slots.TryInsert(atm, BankAtmComponent.CardSlotId, card, SPlayer), Is.True,
                    $"card must be reinsertable after eject cycle {cycle}");
            });
            await RunTicks(2);
        }

        await Server.WaitPost(() =>
        {
            // Closing the UI must not affect the physical card.
            SEntMan.EventBus.RaiseLocalEvent(atm,
                new BoundUIClosedEvent(BankAtmUiKey.Key, atm, SPlayer));
            Assert.That(slots.GetItemOrNull(atm, BankAtmComponent.CardSlotId), Is.EqualTo(card));
            Assert.That(SEntMan.EntityExists(card), Is.True);

            // End a bank session, then eject. Session teardown must not touch
            // the physical card.
            SEntMan.EventBus.RaiseLocalEvent(atm, new BankAtmAuthMessage(account.Pin) { Actor = SPlayer });
            SEntMan.EventBus.RaiseLocalEvent(atm, new BankAtmEndSessionMessage { Actor = SPlayer });
            Assert.That(slots.GetItemOrNull(atm, BankAtmComponent.CardSlotId), Is.EqualTo(card));

            SEntMan.EventBus.RaiseLocalEvent(atm, new BankAtmEjectMessage { Actor = SPlayer });
            Assert.That(slots.GetItemOrNull(atm, BankAtmComponent.CardSlotId), Is.Null);
            Assert.That(SEntMan.EntityExists(card), Is.True);

            // Leave a card inside while the ATM is deleted. The slot container
            // must not take the card with it.
            Assert.That(slots.TryInsert(atm, BankAtmComponent.CardSlotId, card, SPlayer), Is.True);
            SEntMan.DeleteEntity(atm);
        });
        await RunTicks(3);

        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.EntityExists(card), Is.True,
                "deleting an ATM with an inserted card must not delete the card");
            Assert.That(SEntMan.GetComponent<TransformComponent>(card).ParentUid,
                Is.Not.EqualTo(EntityUid.Invalid), "released card must remain in the world");
        });
    }

    [Test]
    public async Task AtmRejectsWrongPin()
    {
        await SetupAccount(500);
        EntityUid atm = default;
        EntityUid card = default;
        EconomyAccountComponent account = default!;

        await Server.WaitPost(() =>
        {
            SEntMan.System<EconomyBankSystem>().TryGetPlayerAccount(SPlayer, out _, out account);
            var coords = SEntMan.GetCoordinates(TargetCoords);
            atm = SEntMan.SpawnEntity("BankATM", coords);
            card = SEntMan.SpawnEntity("PassengerIDCard", coords);
            SEntMan.GetComponent<IdCardComponent>(card).BankAccountId = account.AccountId;
            SEntMan.System<ItemSlotsSystem>().TryInsert(atm, BankAtmComponent.CardSlotId, card, SPlayer);
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            var auth = new BankAtmAuthMessage("0000") { Actor = SPlayer };
            SEntMan.EventBus.RaiseLocalEvent(atm, auth);

            var request = new BankTransactionRequest(502, BankOperation.Withdraw, 100);
            SEntMan.EventBus.RaiseLocalEvent(atm, new BankAtmTransactionMessage(request) { Actor = SPlayer });
        });
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            SEntMan.System<EconomyBankSystem>().TryGetPlayerAccount(SPlayer, out _, out var current);
            Assert.That(current.Balance, Is.EqualTo(500));
        });
    }

}
