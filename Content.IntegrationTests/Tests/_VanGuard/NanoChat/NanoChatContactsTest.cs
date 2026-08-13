#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
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
///     Verifies the NanoChat station directory behaviour from the original
///     VG/ADT build: every card that has an owner (a full name on its ID) and
///     is on the station is listed. Cards without a name, cards that opted out
///     of the directory and cards on other grids must not show up.
/// </summary>
public sealed class NanoChatContactsTest : InteractionTest
{
    [Test]
    public async Task Directory_ListsAllCardsWithAnOwnerOnTheStation()
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

        // A PDA with a NanoChat card + cartridge, as a loader for the directory UI.
        await SpawnTarget("PassengerPDA");
        var pda = ToServer(Target)!.Value;
        var pdaCard = SEntMan.GetComponent<PdaComponent>(pda).ContainedId!.Value;

        // The registered card has an owner (full name) - it must be listed.
        await Server.WaitPost(() =>
        {
            SEntMan.GetComponent<IdCardComponent>(pdaCard).FullName = "Alice Johnson";
        });

        // A second card on the same station with an owner - also listed,
        // even though it has no station record (like a guest badge or a pet).
        EntityUid guestCard = default;
        EntityUid noNameCard = default;
        EntityUid hiddenCard = default;
        EntityUid offStationCard = default;
        await Server.WaitPost(() =>
        {
            guestCard = SEntMan.SpawnEntity("PassengerIDCard", SEntMan.GetCoordinates(TargetCoords));
            SEntMan.GetComponent<IdCardComponent>(guestCard).FullName = "Pun Pun";

            noNameCard = SEntMan.SpawnEntity("PassengerIDCard", SEntMan.GetCoordinates(TargetCoords));

            hiddenCard = SEntMan.SpawnEntity("PassengerIDCard", SEntMan.GetCoordinates(TargetCoords));
            SEntMan.GetComponent<IdCardComponent>(hiddenCard).FullName = "Ghost Employee";
            SEntMan.GetComponent<NanoChatCardComponent>(hiddenCard).ListNumber = false;

            // A card that is not on the station's grid (far away from it).
            offStationCard = SEntMan.SpawnEntity("PassengerIDCard",
                new EntityCoordinates(MapData.MapUid, new Vector2(1000, 1000)));
            SEntMan.GetComponent<IdCardComponent>(offStationCard).FullName = "Off Station Joe";
        });
        await RunTicks(2);

        Assert.That(SEntMan.GetComponent<NanoChatCardComponent>(pdaCard).Number, Is.Not.Null);
        Assert.That(SEntMan.GetComponent<NanoChatCardComponent>(guestCard).Number, Is.Not.Null);
        Assert.That(SEntMan.GetComponent<NanoChatCardComponent>(noNameCard).Number, Is.Not.Null);
        Assert.That(SEntMan.GetComponent<NanoChatCardComponent>(hiddenCard).Number, Is.Not.Null);
        Assert.That(SEntMan.GetComponent<NanoChatCardComponent>(offStationCard).Number, Is.Not.Null);

        // Grab a NanoChat cartridge that is installed in some PDA on the station.
        // The directory does not depend on which card sits in the loader's PDA, so
        // any working loader lets us exercise the real UpdateUI flow.
        EntityUid cartridgeUid = default;
        NanoChatCartridgeComponent cartridge = default!;
        EntityUid loader = default;
        var query = SEntMan.EntityQueryEnumerator<NanoChatCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out var comp, out var cartComp))
        {
            if (cartComp.LoaderUid is not { } cartLoader)
                continue;

            cartridgeUid = uid;
            cartridge = comp;
            loader = cartLoader;
            break;
        }

        Assert.That(cartridgeUid, Is.Not.EqualTo(EntityUid.Invalid), "No NanoChat cartridge found in the test scene.");

        // Push the directory state exactly like the real cartridge flow does.
        await Server.WaitPost(() =>
        {
            var system = SEntMan.System<NanoChatCartridgeSystem>();
            var updateUi = typeof(NanoChatCartridgeSystem).GetMethod("UpdateUI",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            updateUi.Invoke(system, new object[] { new Entity<NanoChatCartridgeComponent>(cartridgeUid, cartridge), loader });
        });
        await RunTicks(2);

        // Read the state the server pushed into the loader's BUI.
        var uiComp = SEntMan.GetComponent<UserInterfaceComponent>(loader);
        var states = (Dictionary<Enum, BoundUserInterfaceState>)
            typeof(UserInterfaceComponent).GetField("States", BindingFlags.Public | BindingFlags.Instance)!.GetValue(uiComp)!;
        var state = (NanoChatUiState) states[PdaUiKey.Key];

        var registeredNumber = SEntMan.GetComponent<NanoChatCardComponent>(pdaCard).Number!.Value;
        var guestNumber = SEntMan.GetComponent<NanoChatCardComponent>(guestCard).Number!.Value;
        var noNameNumber = SEntMan.GetComponent<NanoChatCardComponent>(noNameCard).Number!.Value;
        var hiddenNumber = SEntMan.GetComponent<NanoChatCardComponent>(hiddenCard).Number!.Value;
        var offStationNumber = SEntMan.GetComponent<NanoChatCardComponent>(offStationCard).Number!.Value;

        Assert.That(state.Contacts, Is.Not.Null, "The directory state must include the contacts list.");

        var numbers = state.Contacts!.Select(c => c.Number).ToHashSet();

        Assert.That(numbers, Does.Contain(registeredNumber), "The registered card must appear in the directory.");
        Assert.That(numbers, Does.Contain(guestNumber),
            "A card with an owner but no station record (e.g. Pun Pun, a pet monkey) must still appear.");
        Assert.That(numbers, Does.Not.Contain(noNameNumber),
            "A card without a full name must NOT appear in the directory.");
        Assert.That(numbers, Does.Not.Contain(hiddenNumber),
            "A card that opted out of the directory must NOT appear.");
        Assert.That(numbers, Does.Not.Contain(offStationNumber),
            "A card on another grid must NOT appear in this station's directory.");
    }
}
