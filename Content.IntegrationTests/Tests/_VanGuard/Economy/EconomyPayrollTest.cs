#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._VanGuard.Economy.Components;
using Content.Server._VanGuard.Economy.Systems;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.Humanoid;
using Content.Shared.Materials;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Economy;

/// <summary>
///     Verifies the station economy: personal bank accounts, payroll deposits,
///     station-budget payroll and the market sell-price modifiers.
/// </summary>
[TestFixture]
public sealed class EconomyPayrollTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: playTimeTracker
  id: EconomyTestTrackerA

- type: playTimeTracker
  id: EconomyTestTrackerB

- type: job
  id: EconomyTestPaidJob
  name: economy test paid job
  playTimeTracker: EconomyTestTrackerA
  salary: 1000

- type: job
  id: EconomyTestBudgetJob
  name: economy test budget job
  playTimeTracker: EconomyTestTrackerB
  salary: 2000
  payrollFromStationBudget: true
";

    [Test]
    public async Task StartingPayroll_DepositedOnJobAdd()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        var (mind, _) = await SpawnEmployee(server, entMan, coords, "EconomyTestPaidJob");

        var account = entMan.GetComponent<EconomyAccountComponent>(mind);
        Assert.That(account.Balance, Is.EqualTo(1000),
            "Adding a job with a salary must pay the starting payroll into the account.");
        Assert.That(account.JobId, Is.EqualTo("EconomyTestPaidJob"));
    }

    [Test]
    public async Task Payroll_ProcessDepositsDirectSalary()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        var (mind, _) = await SpawnEmployee(server, entMan, coords, "EconomyTestPaidJob");

        // Isolate the payroll pass from the already-paid starting payroll.
        await server.WaitPost(() =>
        {
            var account = entMan.GetComponent<EconomyAccountComponent>(mind);
            account.StartingPayrollReceived = true;
            account.Balance = 0;
            entMan.System<EconomyPayrollSystem>().ProcessPayroll();
        });
        await server.WaitRunTicks(2);

        var account = entMan.GetComponent<EconomyAccountComponent>(mind);
        Assert.That(account.Balance, Is.EqualTo(1000),
            "ProcessPayroll must deposit the job salary into the personal account.");
    }

    [Test]
    public async Task Payroll_FromStationBudget_DeductsAndCaps()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();

        // A station that owns the test grid, with a cargo account balance.
        EntityUid station = default;
        await server.WaitPost(() =>
        {
            station = entMan.SpawnEntity(null, new EntityCoordinates(testMap.MapUid, default));
            entMan.AddComponent<StationDataComponent>(station);
            entMan.AddComponent<StationBankAccountComponent>(station);
            entMan.System<StationSystem>().AddGridToStation(station, testMap.Grid);
        });
        await server.WaitRunTicks(2);

        // The employee body must live on the station's grid.
        var coords = new EntityCoordinates(testMap.Grid, default);
        var (mind, _) = await SpawnEmployee(server, entMan, coords, "EconomyTestBudgetJob");

        await server.WaitPost(() =>
        {
            var account = entMan.GetComponent<EconomyAccountComponent>(mind);
            account.StartingPayrollReceived = true;
            account.Balance = 0;
            entMan.System<EconomyPayrollSystem>().ProcessPayroll();
        });
        await server.WaitRunTicks(2);

        var stationBank = entMan.GetComponent<StationBankAccountComponent>(station);
        Assert.That(entMan.GetComponent<EconomyAccountComponent>(mind).Balance, Is.EqualTo(2000),
            "The full salary must be paid when the station budget covers it.");
        Assert.That(entMan.System<CargoSystem>().GetBalanceFromAccount((station, stationBank), stationBank.PrimaryAccount),
            Is.EqualTo(0), "The salary must be deducted from the station cargo account.");
    }

    [Test]
    public async Task Payroll_FromStationBudget_InsufficientFunds_PaysNothing()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();

        EntityUid station = default;
        await server.WaitPost(() =>
        {
            station = entMan.SpawnEntity(null, new EntityCoordinates(testMap.MapUid, default));
            entMan.AddComponent<StationDataComponent>(station);
            var bank = entMan.AddComponent<StationBankAccountComponent>(station);
            entMan.System<StationSystem>().AddGridToStation(station, testMap.Grid);
            // Bankrupt the station by draining its primary account.
            var cargo = entMan.System<CargoSystem>();
            cargo.UpdateBankAccount((station, bank), -cargo.GetBalanceFromAccount((station, bank), bank.PrimaryAccount), bank.PrimaryAccount);
        });
        await server.WaitRunTicks(2);

        var coords = new EntityCoordinates(testMap.Grid, default);
        var (mind, _) = await SpawnEmployee(server, entMan, coords, "EconomyTestBudgetJob");

        await server.WaitPost(() =>
        {
            var account = entMan.GetComponent<EconomyAccountComponent>(mind);
            account.StartingPayrollReceived = true;
            account.Balance = 0;
            entMan.System<EconomyPayrollSystem>().ProcessPayroll();
        });
        await server.WaitRunTicks(2);

        Assert.That(entMan.GetComponent<EconomyAccountComponent>(mind).Balance, Is.EqualTo(0),
            "A bankrupt station must not mint money for payroll.");
    }

    [Test]
    public async Task Payroll_SkipsWhenNoUserId()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        // Mind without a player session (UserId == null) is not a real crew member.
        var (mind, _) = await SpawnEmployee(server, entMan, coords, "EconomyTestPaidJob", withUserId: false);

        await server.WaitPost(() =>
        {
            var account = entMan.GetComponent<EconomyAccountComponent>(mind);
            account.StartingPayrollReceived = true;
            account.Balance = 0;
            entMan.System<EconomyPayrollSystem>().ProcessPayroll();
        });
        await server.WaitRunTicks(2);

        Assert.That(entMan.GetComponent<EconomyAccountComponent>(mind).Balance, Is.EqualTo(0),
            "Minds without a player user id must not be paid.");
    }


    [Test]
    public async Task Bank_DepositWithdrawTransfer()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        var bank = entMan.System<EconomyBankSystem>();
        EntityUid mindA = default;
        EntityUid mindB = default;
        await server.WaitPost(() =>
        {
            mindA = entMan.System<SharedMindSystem>().CreateMind(null).Owner;
            mindB = entMan.System<SharedMindSystem>().CreateMind(null).Owner;
            var accountA = bank.EnsureAccount(mindA);
            var accountB = bank.EnsureAccount(mindB);

            Assert.That(bank.Deposit((mindA, accountA), 500, "test-deposit"), Is.True);
            Assert.That(bank.Withdraw((mindA, accountA), 120, "test-withdraw"), Is.True);
            Assert.That(accountA.Balance, Is.EqualTo(380));

            // Transfer drains the source and credits the destination.
            Assert.That(bank.Transfer((mindA, accountA), (mindB, accountB), 80, "test-transfer"), Is.True);
            Assert.That(accountA.Balance, Is.EqualTo(300));
            Assert.That(accountB.Balance, Is.EqualTo(80));

            // Overdraw must fail and change nothing.
            Assert.That(bank.Withdraw((mindA, accountA), 9999, "test-overdraw"), Is.False);
            Assert.That(accountA.Balance, Is.EqualTo(300));

            // Invalid amounts are rejected.
            Assert.That(bank.Deposit((mindA, accountA), -10, "test-negative"), Is.False);
            Assert.That(accountA.Balance, Is.EqualTo(300));
        });
        await server.WaitRunTicks(2);
    }

    [Test]
    public async Task Market_AdjustSellPrice_AppliesMaterialModifiers()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();
        var coords = new EntityCoordinates(testMap.MapUid, default);

        var market = entMan.System<EconomyMarketSystem>();
        EntityUid station = default;
        EntityUid goods = default;
        await server.WaitPost(() =>
        {
            station = entMan.SpawnEntity(null, coords);
            goods = entMan.SpawnEntity(null, coords);

            // A cargo crate made purely of steel.
            var composition = entMan.AddComponent<PhysicalCompositionComponent>(goods);
            composition.MaterialComposition["Steel"] = 10;

            market.SetMarketModifiers(station, new Dictionary<string, float> { { "Steel", 2.0f } });

            var adjusted = market.AdjustSellPrice(station, goods, 100);
            Assert.That(adjusted, Is.EqualTo(200.0).Within(0.01),
                "A 2x steel multiplier must double the sell price of a steel-only item.");

            // No modifiers -> base price.
            market.ClearMarketModifiers(station);
            Assert.That(market.AdjustSellPrice(station, goods, 100), Is.EqualTo(100.0).Within(0.01));
        });
        await server.WaitRunTicks(2);
    }


    private async Task<(EntityUid Mind, EntityUid Body)> SpawnEmployee(
        Robust.UnitTesting.IServerIntegrationInstance server,
        IEntityManager entMan,
        EntityCoordinates coords,
        string jobId,
        NetUserId? userId = null,
        bool withUserId = true)
    {
        EntityUid mindUid = default;
        EntityUid body = default;
        await server.WaitPost(() =>
        {
            body = entMan.SpawnEntity("MobHuman", coords);
            entMan.EnsureComponent<MindContainerComponent>(body);
            entMan.EnsureComponent<HumanoidProfileComponent>(body);

            var mindSystem = entMan.System<SharedMindSystem>();
            var mind = mindSystem.CreateMind(null);
            if (withUserId)
            {
                // MindSystem.SetUserId only accepts ids registered with the
                // PlayerManager, so hand in the test client session's id.
                userId ??= Client.Session!.UserId;
                mindSystem.SetUserId(mind.Owner, userId, mind.Comp);
            }

            mindSystem.TransferTo(mind, body, mind: mind);
            entMan.System<SharedRoleSystem>().MindAddJobRole(mind.Owner, mind.Comp, silent: true, jobPrototype: jobId);

            mindUid = mind.Owner;
        });
        await server.WaitRunTicks(3);
        return (mindUid, body);
    }
}

