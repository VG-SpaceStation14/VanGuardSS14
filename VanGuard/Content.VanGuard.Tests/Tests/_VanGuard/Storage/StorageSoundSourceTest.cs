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
        EntityUid box = default;
        EntityUid backpack = default;
        EntityUid medkit = default;

        await Server.WaitPost(() =>
        {
            box = SSpawn("BoxCardboard");
            backpack = SSpawn("ClothingBackpack");
            medkit = SSpawn("Medkit");

            var containers = SEntMan.System<SharedContainerSystem>();
            var boxStorage = SEntMan.GetComponent<StorageComponent>(box);
            var backpackStorage = SEntMan.GetComponent<StorageComponent>(backpack);

            Assert.That(containers.Insert(medkit, backpackStorage.Container), Is.True);
            Assert.That(containers.Insert(backpack, boxStorage.Container), Is.True);
        });

        await Server.WaitAssertion(() =>
        {
            var containers = SEntMan.System<SharedContainerSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(StorageSoundSource.Resolve(SEntMan, containers, medkit), Is.EqualTo(box));
                Assert.That(StorageSoundSource.Resolve(SEntMan, containers, backpack), Is.EqualTo(box));
                Assert.That(StorageSoundSource.Resolve(SEntMan, containers, box), Is.EqualTo(box));
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
