#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Access.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Roles.Components;
using Content.Shared.Roles.Jobs;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Economy;

/// <summary>
///     Verifies that changing a job through the ID card console also updates
///     the mind's job role, so payroll follows the new job.
/// </summary>
public sealed class IdCardConsoleJobChangeTest : InteractionTest
{
    private const string ConsoleProtoId = "ComputerId";
    private const string TargetJobId = "Janitor";

    [Test]
    public async Task ChangingJobViaConsole_UpdatesMindJobRole()
    {
        // Give the player a mind with an initial job and a bank account. The
        // card's BankAccountId binds it to that account (and thus the mind),
        // which is how the console resolves the owner.
        var mindSystem = SEntMan.System<SharedMindSystem>();
        var roleSystem = SEntMan.System<Content.Server.Roles.RoleSystem>();
        var jobSystem = SEntMan.System<SharedJobSystem>();
        var bank = SEntMan.System<Content.Server._VanGuard.Economy.Systems.EconomyBankSystem>();
        EntityUid mindUid = default;
        await Server.WaitPost(() =>
        {
            var mind = mindSystem.CreateMind(ClientSession.UserId, "Test User");
            mindUid = mind.Owner;
            mindSystem.SetUserId(mind.Owner, ClientSession.UserId, mind.Comp);
            mindSystem.TransferTo(mind, SPlayer, mind: mind);
            roleSystem.MindAddJobRole(mindUid, mind.Comp, silent: true, jobPrototype: "Passenger");
        });
        await RunTicks(2);

        Assert.That(jobSystem.MindTryGetJob(mindUid, out var initialJob) && initialJob.ID == "Passenger",
            "Player should start as Passenger.");

        // Put an ID card bound to the mind's bank account in the player's hand;
        // the console will treat it as the target.
        EntityUid card = default;
        await Server.WaitPost(() =>
        {
            var account = bank.EnsureAccount(mindUid, SEntMan.GetComponent<MindComponent>(mindUid));
            card = SEntMan.SpawnEntity("PassengerIDCard", SEntMan.GetCoordinates(TargetCoords));
            SEntMan.GetComponent<IdCardComponent>(card).BankAccountId = account.AccountId;
            var hands = SEntMan.System<SharedHandsSystem>();
            hands.TryPickup(SPlayer, card);
        });
        await RunTicks(2);

        // Spawn the ID console and insert the player's card into its target slot.
        EntityUid console = default;
        await Server.WaitPost(() =>
        {
            console = SEntMan.SpawnAtPosition(ConsoleProtoId, SEntMan.GetCoordinates(TargetCoords));
            var slotSys = SEntMan.System<ItemSlotsSystem>();
            slotSys.TryInsert(console, IdCardConsoleComponent.TargetIdCardSlotId, card, SPlayer);
        });
        await RunTicks(2);

        // The console has no AccessReader in this prototype, so any privileged
        // card authorizes it. Give it one so the write is allowed.
        await Server.WaitPost(() =>
        {
            var slotSys = SEntMan.System<ItemSlotsSystem>();
            var priv = SEntMan.SpawnEntity("CaptainIDCard", SEntMan.GetCoordinates(TargetCoords));
            slotSys.TryInsert(console, IdCardConsoleComponent.PrivilegedIdCardSlotId, priv, SPlayer);
        });
        await RunTicks(2);

        // Send the write message with a new job. The BUI must be open on the
        // client for SendBui to work, so invoke the server handler directly
        // (mirrors the AFK system test pattern).
        await Server.WaitPost(() =>
        {
            var msg = new IdCardConsoleComponent.WriteToTargetIdMessage(
                "Test User",
                "Janitor",
                new(),
                new ProtoId<JobPrototype>(TargetJobId))
            {
                Actor = SPlayer,
            };

            var system = SEntMan.System<IdCardConsoleSystem>();
            var method = typeof(IdCardConsoleSystem).GetMethod(
                "OnWriteToTargetIdMessage",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var component = SEntMan.GetComponent<IdCardConsoleComponent>(console);
            method.Invoke(system, [console, component, msg]);
        });
        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            Assert.That(jobSystem.MindTryGetJob(mindUid, out var updatedJob),
                "The mind should still have a job after the console write.");
            Assert.That(updatedJob!.ID, Is.EqualTo(TargetJobId),
                "The mind's job role must be updated to the job set on the ID console.");
        });
    }
}
