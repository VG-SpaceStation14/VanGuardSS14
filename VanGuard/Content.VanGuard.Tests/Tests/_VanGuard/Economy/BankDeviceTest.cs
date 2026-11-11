#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._VanGuard.Economy.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Content.Shared._VanGuard.Economy;

namespace Content.IntegrationTests.Tests._VanGuard.Economy;

[TestFixture]
public sealed class BankDeviceTest : InteractionTest
{
    [Test]
    public async Task BankCartridgeIsPreinstalledInPda()
    {
        EntityUid pda = default;
        var loader = SEntMan.System<CartridgeLoaderSystem>();

        await Server.WaitPost(() =>
        {
            pda = SEntMan.SpawnEntity("PassengerPDA", SEntMan.GetCoordinates(TargetCoords));
        });
        await RunTicks(2);

        Assert.That(loader.HasProgram<BankCartridgeComponent>((pda, SEntMan.GetComponent<CartridgeLoaderComponent>(pda))), Is.True,
            "the bank cartridge must be a preinstalled PDA program");
    }

    [Test]
    public async Task AtmAndIdCardAreOrdinaryWorldEntities()
    {
        EntityUid atm = default;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            atm = SEntMan.SpawnEntity("BankATM", coords);
            var card = SEntMan.SpawnEntity("PassengerIDCard", coords);

            Assert.That(SEntMan.HasComponent<BankAtmComponent>(atm), Is.True);
            Assert.That(SEntMan.HasComponent<ActivatableUIComponent>(card), Is.False,
                "an ID card must not open the legacy bank wallet UI");
        });

        // Let map initialization and the UserInterface component finish before
        // opening; upstream now initializes UI state on a later tick.
        await RunTicks(2);

        await Server.WaitPost(() =>
        {
            // Opening is power-gated by ActivatableUIRequiresPower. This test
            // only needs to prove that the ATM advertises its BUI key.
            Assert.That(SEntMan.System<UserInterfaceSystem>().HasUi(atm, BankAtmUiKey.Key), Is.True,
                "the ATM must expose its bound user interface key");
        });
    }
}
