#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Content.Client.CartridgeLoader;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._VanGuard.NanoChat;
using Content.Server.Station.Systems;
using Content.Shared._VanGuard.NanoChat;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._VanGuard.NanoChat;

/// <summary>
///     Verifies that a NanoChat cartridge stays fully functional across a PDA
///     power-off / power-on cycle: the client-side cartridge fragment must still
///     receive the directory, existing chats and the open conversation.
/// </summary>
public sealed class NanoChatPowerCycleTest : InteractionTest
{
    [Test]
    public async Task Cartridge_RefreshesAfterPdaPowerCycle()
    {
        var stationSystem = SEntMan.System<StationSystem>();

        // A station that owns the test grid.
        EntityUid station = default;
        await Server.WaitPost(() =>
        {
            station = SEntMan.SpawnEntity(null, new EntityCoordinates(MapData.MapUid, default));
            SEntMan.AddComponent<StationDataComponent>(station);
            stationSystem.AddGridToStation(station, MapData.Grid);
        });
        await RunTicks(2);

        await SpawnTarget("PassengerPDA");
        var pda = ToServer(Target)!.Value;

        // Grab the NanoChat cartridge installed in the PDA.
        EntityUid cartridgeUid = default;
        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<NanoChatCartridgeComponent, CartridgeComponent>();
            while (query.MoveNext(out var uid, out _, out _))
            {
                cartridgeUid = uid;
                break;
            }
        });
        Assert.That(cartridgeUid, Is.Not.EqualTo(EntityUid.Invalid), "No NanoChat cartridge found in the test scene.");

        // Give the ID card an owner so it appears in the directory.
        await Server.WaitPost(() =>
        {
            SEntMan.GetComponent<IdCardComponent>(SEntMan.GetComponent<PdaComponent>(pda).ContainedId!.Value).FullName = "Alice Johnson";
        });
        await RunTicks(2);

        // Simulate an existing conversation: a contact and some messages on the card.
        await Server.WaitPost(() =>
        {
            var card = SEntMan.GetComponent<NanoChatCardComponent>(SEntMan.GetComponent<PdaComponent>(pda).ContainedId!.Value);
            card.Recipients[777] = new NanoChatRecipient(777, "Bob Smith", "Cargo Technician");
            card.Messages[777] = [new NanoChatMessage(TimeSpan.FromMinutes(1), "hello bob", 777)];
            card.CurrentChat = 777;
            SEntMan.Dirty(SEntMan.GetComponent<PdaComponent>(pda).ContainedId!.Value, card);
        });
        await RunTicks(2);

        // Open the PDA and wait for the boot animation to finish. Poll the
        // server's booted flag with a bounded retry instead of sleeping a fixed
        // wall-clock duration.
        await Pickup();
        await UseInHand();
        var booted = false;
        for (var i = 0; i < 100 && !booted; i++)
        {
            await RunTicks(2);
            await Server.WaitPost(() => booted = SEntMan.GetComponent<PdaComponent>(pda).Booted);
        }
        Assert.That(booted, Is.True, "The PDA must finish booting before the cartridge UI can be activated.");
        await RunTicks(10);

        // Activate the NanoChat cartridge, like the player clicking its icon in the program list.
        var cartridgeNet = SEntMan.GetNetEntity(cartridgeUid);
        await SendBui(PdaUiKey.Key, new CartridgeLoaderUiMessage(cartridgeNet, CartridgeUiMessageAction.Activate));
        await RunTicks(30);

        // The client cartridge fragment must have received the state.
        Assert.That((await GetClientFragmentState())?.Contacts, Is.Not.Null.And.Not.Empty,
            "Client fragment must receive contacts on the first open.");

        // Power the PDA off with the PowerOff button.
        await SendBui(PdaUiKey.Key, new PdaPowerOffMessage());
        await RunTicks(30);

        // Reopen the PDA.
        await UseInHand();
        await RunTicks(30);

        // The client fragment must be fully refreshed after the power cycle.
        var postCycleState = await GetClientFragmentState();
        Assert.That(postCycleState, Is.Not.Null, "Client cartridge fragment is missing after the power cycle.");
        Assert.That(postCycleState!.Contacts, Is.Not.Null.And.Not.Empty,
            "Client fragment must receive contacts after the power cycle.");
        Assert.That(postCycleState.Recipients.ContainsKey(777), Is.True,
            "Existing chats must be present in the client fragment after the power cycle.");
        Assert.That(postCycleState.CurrentChat, Is.EqualTo(777),
            "The open chat must survive the power cycle.");
    }

    /// <summary>
    ///     Reads the state currently held by the client-side NanoChat fragment
    ///     (the same data the cartridge UI is rendering).
    /// </summary>
    private async Task<NanoChatUiState?> GetClientFragmentState()
    {
        NanoChatUiState? result = null;

        await Client.WaitPost(() =>
        {
            Assert.That(CEntMan.TryGetComponent<UserInterfaceComponent>(CEntMan.GetEntity(Target!.Value), out var uiComp),
                "The PDA must have a UI component on the client.");
            Assert.That(uiComp!.ClientOpenInterfaces.TryGetValue(PdaUiKey.Key, out var bui),
                "The PDA UI must be open on the client.");

            var cartridgeUi = typeof(CartridgeLoaderBoundUserInterface)
                .GetField("_activeCartridgeUI", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(bui);
            Assert.That(cartridgeUi, Is.Not.Null, "The active cartridge UI field must be set on the client BUI.");

            var fragment = cartridgeUi!.GetType()
                .GetField("_fragment", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(cartridgeUi);
            Assert.That(fragment, Is.Not.Null, "The cartridge fragment field must be set on the client cartridge UI.");

            result = fragment!.GetType()
                .GetField("_lastState", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(fragment) as NanoChatUiState;
            Assert.That(result, Is.Not.Null, "The cartridge fragment must hold a NanoChat UI state.");
        });

        return result;
    }
}
