#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared._VanGuard.Storage;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VanGuard.Storage;

/// <summary>
///     Covers sound source resolution for storage entities nested in other containers.
/// </summary>
[TestFixture]
public sealed class StorageSoundSourceTest : GameTest
{
    [Test]
    public async Task NestedStorageResolvesToOutermostContainer()
    {
        EntityUid outer = default;
        EntityUid backpack = default;
        EntityUid medkit = default;

        await Server.WaitPost(() =>
        {
            outer = SSpawn("ClothingBackpackSatchelAdmin");
            backpack = SSpawn("ClothingBackpack");
            medkit = SSpawn("Medkit");

            var containers = SEntMan.System<SharedContainerSystem>();
            var outerStorage = SEntMan.GetComponent<StorageComponent>(outer);
            var backpackStorage = SEntMan.GetComponent<StorageComponent>(backpack);

            Assert.That(containers.Insert(medkit, backpackStorage.Container), Is.True);
            Assert.That(containers.Insert(backpack, outerStorage.Container), Is.True);
        });

        await Server.WaitAssertion(() =>
        {
            var containers = SEntMan.System<SharedContainerSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(StorageSoundSource.Resolve(SEntMan, containers, medkit), Is.EqualTo(outer));
                Assert.That(StorageSoundSource.Resolve(SEntMan, containers, backpack), Is.EqualTo(outer));
                Assert.That(StorageSoundSource.Resolve(SEntMan, containers, outer), Is.EqualTo(outer));
            });
        });
    }

    [Test]
    public async Task DirectlyContainedStorageResolvesToHolder()
    {
        EntityUid backpack = default;
        EntityUid medkit = default;

        await Server.WaitPost(() =>
        {
            backpack = SSpawn("ClothingBackpack");
            medkit = SSpawn("Medkit");

            var containers = SEntMan.System<SharedContainerSystem>();
            var backpackStorage = SEntMan.GetComponent<StorageComponent>(backpack);

            Assert.That(containers.Insert(medkit, backpackStorage.Container), Is.True);
        });

        await Server.WaitAssertion(() =>
        {
            var containers = SEntMan.System<SharedContainerSystem>();
            Assert.That(StorageSoundSource.Resolve(SEntMan, containers, medkit), Is.EqualTo(backpack));
        });
    }
}
