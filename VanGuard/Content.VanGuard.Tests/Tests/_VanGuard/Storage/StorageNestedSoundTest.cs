#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Storage;
using Content.Shared.Timing.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VanGuard.Storage;

/// <summary>
///     Verifies that storage open and close sounds use a source with a valid world position
///     for nested, held, and ground-placed containers.
/// </summary>
[TestFixture]
public sealed class StorageNestedSoundTest : InteractionTest
{
    [Test]
    public async Task NestedStorageSoundsUseOuterContainerSource()
    {
        EntityUid backpack = default;
        EntityUid toolbox = default;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(PlayerCoords);
            backpack = SSpawnAtPosition("ClothingBackpackHolding", coords);
            toolbox = SSpawnAtPosition("ToolboxMechanical", coords);

            var containers = SEntMan.System<SharedContainerSystem>();
            var backpackStorage = SEntMan.GetComponent<StorageComponent>(backpack);

            Assert.That(containers.Insert(toolbox, backpackStorage.Container), Is.True,
                "the toolbox should be inserted into the backpack");

            SEntMan.RemoveComponent<UseDelayComponent>(toolbox);
        });

        await Server.WaitPost(() =>
        {
            var storageSystem = Server.System<StorageSystem>();
            var uiSystem = Server.System<UserInterfaceSystem>();
            var backpackStorage = SEntMan.GetComponent<StorageComponent>(backpack);
            var toolboxStorage = SEntMan.GetComponent<StorageComponent>(toolbox);

            // The nested UI is opened from the parent storage UI, as it happens in game.
            storageSystem.OpenStorageUI(backpack, SPlayer, backpackStorage, silent: false);
            Assert.That(uiSystem.IsUiOpen(backpack, StorageComponent.StorageUiKey.Key, SPlayer), Is.True,
                "the parent storage UI should be open");

            var beforeOpen = GetAudioEntities();

            storageSystem.OpenStorageUI(toolbox, SPlayer, toolboxStorage, silent: false);

            var openSounds = GetNewAudioEntities(beforeOpen);
            Assert.That(openSounds, Has.Count.EqualTo(1), "expected exactly one storage open sound");
            Assert.That(GetParent(openSounds[0]), Is.EqualTo(backpack),
                "the open sound should originate from the outermost container");

            var beforeClose = GetAudioEntities();

            uiSystem.CloseUi(toolbox, StorageComponent.StorageUiKey.Key, SPlayer);

            var closeSounds = GetNewAudioEntities(beforeClose);
            Assert.That(closeSounds, Has.Count.EqualTo(1), "expected exactly one storage close sound");
            Assert.That(GetParent(closeSounds[0]), Is.EqualTo(backpack),
                "the close sound should originate from the outermost container");
        });
    }

    [Test]
    public async Task GroundStorageSoundUsesStorageItself()
    {
        EntityUid storageEntity = default;

        await Server.WaitPost(() =>
        {
            storageEntity = SSpawnAtPosition("DiskCase", SEntMan.GetCoordinates(PlayerCoords));
            SEntMan.RemoveComponent<UseDelayComponent>(storageEntity);
        });

        await Server.WaitPost(() =>
        {
            var storageSystem = Server.System<StorageSystem>();
            var uiSystem = Server.System<UserInterfaceSystem>();
            var storage = SEntMan.GetComponent<StorageComponent>(storageEntity);

            var beforeOpen = GetAudioEntities();
            storageSystem.OpenStorageUI(storageEntity, SPlayer, storage, silent: false);

            var openSounds = GetNewAudioEntities(beforeOpen);
            Assert.That(openSounds, Has.Count.EqualTo(1), "expected exactly one ground storage open sound");
            Assert.That(GetParent(openSounds[0]), Is.EqualTo(storageEntity));

            var beforeClose = GetAudioEntities();
            uiSystem.CloseUi(storageEntity, StorageComponent.StorageUiKey.Key, SPlayer);

            var closeSounds = GetNewAudioEntities(beforeClose);
            Assert.That(closeSounds, Has.Count.EqualTo(1), "expected exactly one ground storage close sound");
            Assert.That(GetParent(closeSounds[0]), Is.EqualTo(storageEntity));
        });
    }

    [Test]
    public async Task HeldStorageSoundUsesPlayerSource()
    {
        var held = await PlaceInHands("DiskCase");
        var storageEntity = ToServer(held);

        await Server.WaitPost(() => SEntMan.RemoveComponent<UseDelayComponent>(storageEntity));

        await Server.WaitPost(() =>
        {
            var storageSystem = Server.System<StorageSystem>();
            var uiSystem = Server.System<UserInterfaceSystem>();
            var storage = SEntMan.GetComponent<StorageComponent>(storageEntity);

            var beforeOpen = GetAudioEntities();
            storageSystem.OpenStorageUI(storageEntity, SPlayer, storage, silent: false);

            var openSounds = GetNewAudioEntities(beforeOpen);
            Assert.That(openSounds, Has.Count.EqualTo(1), "expected exactly one held storage open sound");
            Assert.That(GetParent(openSounds[0]), Is.EqualTo(SPlayer));

            var beforeClose = GetAudioEntities();
            uiSystem.CloseUi(storageEntity, StorageComponent.StorageUiKey.Key, SPlayer);

            var closeSounds = GetNewAudioEntities(beforeClose);
            Assert.That(closeSounds, Has.Count.EqualTo(1), "expected exactly one held storage close sound");
            Assert.That(GetParent(closeSounds[0]), Is.EqualTo(SPlayer));
        });
    }

    [Test]
    public async Task MedkitInBackpackPlaysOpenSound()
    {
        EntityUid backpack = default;
        EntityUid medkit = default;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(PlayerCoords);
            backpack = SSpawnAtPosition("ClothingBackpack", coords);
            medkit = SSpawnAtPosition("Medkit", coords);

            var containers = SEntMan.System<SharedContainerSystem>();
            var backpackStorage = SEntMan.GetComponent<StorageComponent>(backpack);

            Assert.That(containers.Insert(medkit, backpackStorage.Container), Is.True,
                "the medkit should be inserted into the backpack");

            SEntMan.RemoveComponent<UseDelayComponent>(medkit);
        });

        await Server.WaitPost(() =>
        {
            var storageSystem = Server.System<StorageSystem>();
            var uiSystem = Server.System<UserInterfaceSystem>();
            var backpackStorage = SEntMan.GetComponent<StorageComponent>(backpack);
            var medkitStorage = SEntMan.GetComponent<StorageComponent>(medkit);

            storageSystem.OpenStorageUI(backpack, SPlayer, backpackStorage, silent: false);
            var beforeOpen = GetAudioEntities();

            storageSystem.OpenStorageUI(medkit, SPlayer, medkitStorage, silent: false);

            var openSounds = GetNewAudioEntities(beforeOpen);
            Assert.That(openSounds, Has.Count.EqualTo(1), "the nested medkit should play its open sound");
            Assert.That(GetParent(openSounds[0]), Is.EqualTo(backpack),
                "the medkit open sound should originate from the backpack");

            var beforeClose = GetAudioEntities();
            uiSystem.CloseUi(medkit, StorageComponent.StorageUiKey.Key, SPlayer);

            // The current medkit prototype only configures an open sound.
            Assert.That(GetNewAudioEntities(beforeClose), Is.Empty,
                "the medkit has no configured close sound");
        });
    }

    private HashSet<EntityUid> GetAudioEntities()
    {
        var result = new HashSet<EntityUid>();
        var query = SEntMan.EntityQueryEnumerator<AudioComponent>();
        while (query.MoveNext(out var uid, out _))
            result.Add(uid);

        return result;
    }

    private List<EntityUid> GetNewAudioEntities(HashSet<EntityUid> before)
    {
        return GetAudioEntities().Where(uid => !before.Contains(uid)).ToList();
    }

    private EntityUid GetParent(EntityUid audio)
    {
        return SEntMan.GetComponent<TransformComponent>(audio).ParentUid;
    }
}
