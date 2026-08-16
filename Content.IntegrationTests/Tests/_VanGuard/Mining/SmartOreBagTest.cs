#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._VanGuard.Mining.OreBags;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VanGuard.Mining;

/// <summary>
/// Verifies that smart ore bags skip ores in their ignore list when magnetically
/// collecting nearby ore.
/// </summary>
[TestFixture]
public sealed class SmartOreBagTest : InteractionTest
{
    [Test]
    public async Task SmartOreBagFiltersIgnoredOres()
    {
        var bag = await SpawnEntity("SmartOreBag", SEntMan.GetCoordinates(PlayerCoords));
        await SpawnEntity("SteelOre1", SEntMan.GetCoordinates(PlayerCoords));
        await SpawnEntity("GoldOre1", SEntMan.GetCoordinates(PlayerCoords));

        // Make the bag magnetize while held and configure it to ignore steel ore.
        await Server.WaitPost(() =>
        {
            var magnet = SEntMan.GetComponent<MagnetPickupComponent>(bag);
            magnet.SlotFlags = null;
            magnet.RequireActiveHand = true;

            SEntMan.GetComponent<SmartOreBagComponent>(bag).IgnoredOres.Add("SteelOre1");
        });

        await Pickup(SEntMan.GetNetEntity(bag));

        // Give the magnet plenty of time to scan and collect nearby ores.
        await RunTicks(250);

        bool steelCollected = false;
        bool goldCollected = false;
        await Server.WaitPost(() =>
        {
            var storage = SEntMan.GetComponent<StorageComponent>(bag);
            foreach (var contained in storage.Container.ContainedEntities)
            {
                var id = SEntMan.GetComponent<MetaDataComponent>(contained).EntityPrototype?.ID;
                if (id == "SteelOre1")
                    steelCollected = true;
                if (id == "GoldOre1")
                    goldCollected = true;
            }
        });

        Assert.That(steelCollected, Is.False, "ignored steel ore should stay on the ground.");
        Assert.That(goldCollected, Is.True, "non-ignored gold ore should be collected by the magnet.");
    }
}
