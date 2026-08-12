#nullable enable
using System.Reflection;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.Mind;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Economy;

/// <summary>
///     Verifies that the cargo request console can deposit personal credits
///     into the station budget and withdraw station funds (large withdrawals
///     require console access).
/// </summary>
public sealed class CargoConsoleStationFundsTest : InteractionTest
{
    private const string ConsoleProtoId = "ComputerCargoOrders";

    private delegate void StationFundsHandler(Entity<CargoOrderConsoleComponent> ent, ref CargoConsoleStationFundsMessage msg);

    [Test]
    public async Task Deposit_MovesPersonalCreditsIntoStationBudget()
    {
        var (station, _) = await SetupStationAndPlayer(5000);

        var bank = SEntMan.System<Content.Server._VanGuard.Economy.Systems.EconomyBankSystem>();
        var cargo = SEntMan.System<CargoSystem>();

        // Player account has 5000; deposit 1200 into the station primary account.
        var playerBalanceBefore = await GetPlayerBalance();
        await InvokeStationFunds(CargoStationFundsAction.Deposit, 1200);

        var stationBank = SEntMan.GetComponent<StationBankAccountComponent>(station);
        var stationBalance = cargo.GetBalanceFromAccount((station, stationBank), stationBank.PrimaryAccount);

        Assert.That(stationBalance, Is.EqualTo(2000 + 1200),
            "Deposited credits must be added to the station primary account.");
        Assert.That(playerBalanceBefore, Is.EqualTo(5000));
        Assert.That(await GetPlayerBalance(), Is.EqualTo(5000 - 1200),
            "Depositing must withdraw the amount from the player's personal account.");
    }

    [Test]
    public async Task Withdraw_BelowThreshold_DoesNotRequireAccess()
    {
        var (station, _) = await SetupStationAndPlayer(1000);
        var cargo = SEntMan.System<CargoSystem>();

        // 1200 < 5000 threshold, so no access needed.
        await InvokeStationFunds(CargoStationFundsAction.Withdraw, 1200);

        var stationBank = SEntMan.GetComponent<StationBankAccountComponent>(station);
        var stationBalance = cargo.GetBalanceFromAccount((station, stationBank), stationBank.PrimaryAccount);

        Assert.That(stationBalance, Is.EqualTo(2000 - 1200),
            "A small withdrawal must come out of the station budget.");
        Assert.That(await GetPlayerBalance(), Is.EqualTo(1000 + 1200),
            "The withdrawn amount must land on the player's personal account.");
    }

    [Test]
    public async Task Withdraw_LargeAmount_RequiresConsoleAccess()
    {
        var (station, _) = await SetupStationAndPlayer(0);
        var cargo = SEntMan.System<CargoSystem>();

        // 6000 >= 5000 threshold and the test player has no Cargo access:
        // the withdrawal must be rejected and nothing changes.
        await InvokeStationFunds(CargoStationFundsAction.Withdraw, 6000);

        var stationBank = SEntMan.GetComponent<StationBankAccountComponent>(station);
        var stationBalance = cargo.GetBalanceFromAccount((station, stationBank), stationBank.PrimaryAccount);

        Assert.That(stationBalance, Is.EqualTo(2000),
            "A large withdrawal without access must not touch the station budget.");
        Assert.That(await GetPlayerBalance(), Is.EqualTo(0),
            "A large withdrawal without access must not credit the player.");
    }

    private async Task<(EntityUid Station, EntityUid Mind)> SetupStationAndPlayer(int playerFunds)
    {
        var stationSystem = SEntMan.System<StationSystem>();
        var bank = SEntMan.System<Content.Server._VanGuard.Economy.Systems.EconomyBankSystem>();
        var mindSystem = SEntMan.System<SharedMindSystem>();

        // A station owning the test grid with a bank account.
        EntityUid station = default;
        await Server.WaitPost(() =>
        {
            station = SEntMan.SpawnEntity(null, new EntityCoordinates(MapData.MapUid, default));
            SEntMan.AddComponent<StationDataComponent>(station);
            SEntMan.AddComponent<StationBankAccountComponent>(station);
            stationSystem.AddGridToStation(station, MapData.Grid);
        });
        await RunTicks(2);

        // Player mind + bank account.
        EntityUid mindUid = default;
        await Server.WaitPost(() =>
        {
            var mind = mindSystem.CreateMind(ClientSession.UserId);
            mindUid = mind.Owner;
            mindSystem.SetUserId(mind.Owner, ClientSession.UserId, mind.Comp);
            mindSystem.TransferTo(mind, SPlayer, mind: mind);

            var account = bank.EnsureAccount(mindUid, mind.Comp);
            bank.Deposit((mindUid, account), playerFunds, "test-funding");
        });
        await RunTicks(2);

        // Spawn the cargo console.
        await Server.WaitPost(() =>
        {
            SEntMan.SpawnAtPosition(ConsoleProtoId, SEntMan.GetCoordinates(TargetCoords));
        });
        await RunTicks(2);

        return (station, mindUid);
    }

    private async Task InvokeStationFunds(CargoStationFundsAction action, int amount)
    {
        await Server.WaitPost(() =>
        {
            EntityUid console = default;
            foreach (var e in SEntMan.System<EntityLookupSystem>()
                         .GetEntitiesInRange(SEntMan.GetCoordinates(TargetCoords), 1f))
            {
                if (SEntMan.HasComponent<CargoOrderConsoleComponent>(e))
                {
                    console = e;
                    break;
                }
            }

            Assert.That(console != default, "Cargo console should have spawned.");

            var msg = new CargoConsoleStationFundsMessage(action, amount)
            {
                Actor = SPlayer,
            };

            var system = SEntMan.System<CargoSystem>();
            var method = typeof(CargoSystem).GetMethod(
                "OnStationFunds",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            // OnStationFunds(Entity<CargoOrderConsoleComponent>, ref CargoConsoleStationFundsMessage)
            var component = SEntMan.GetComponent<CargoOrderConsoleComponent>(console);
            var ent = new Entity<CargoOrderConsoleComponent>(console, component);

            var handler = (StationFundsHandler)method.CreateDelegate(typeof(StationFundsHandler), system);
            handler(ent, ref msg);
        });
        await RunTicks(3);
    }

    private async Task<int> GetPlayerBalance()
    {
        var balance = 0;
        await Server.WaitPost(() =>
        {
            var bank = SEntMan.System<Content.Server._VanGuard.Economy.Systems.EconomyBankSystem>();
            if (bank.TryGetPlayerAccount(SPlayer, out _, out var account))
                balance = account.Balance;
        });
        return balance;
    }
}
