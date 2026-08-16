#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared._VanGuard.Mining;
using Content.Shared.Mining.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Mining;

/// <summary>
/// Verifies that entities with <see cref="InnateMiningScannerViewerComponent"/> (e.g. dwarves)
/// receive ore-detection vision (<see cref="MiningScannerViewerComponent"/>) without holding a
/// handheld mineral scanner, while everyone else does not.
/// </summary>
[TestFixture]
public sealed class InnateMiningScannerTest : GameTest
{
    [Test]
    public async Task Dwarf_HasInnateMiningVision()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();

        var coords = new EntityCoordinates(testMap.MapUid, default);

        var dwarf = await SpawnAndWait(server, entMan, coords, "MobDwarf");

        Assert.That(entMan.HasComponent<InnateMiningScannerViewerComponent>(dwarf), Is.True,
            "Dwarves should have innate mining vision.");
        Assert.That(entMan.HasComponent<MiningScannerViewerComponent>(dwarf), Is.True,
            "Innate mining vision should grant a MiningScannerViewerComponent.");

        var viewer = entMan.GetComponent<MiningScannerViewerComponent>(dwarf);
        var innate = entMan.GetComponent<InnateMiningScannerViewerComponent>(dwarf);
        Assert.That(viewer.ViewRange, Is.EqualTo(innate.ViewRange),
            "Innate viewer range should match the component configuration.");
    }

    [Test]
    public async Task Human_DoesNotGetMiningVisionByDefault()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();

        var coords = new EntityCoordinates(testMap.MapUid, default);

        var human = await SpawnAndWait(server, entMan, coords, "MobHuman");

        Assert.That(entMan.HasComponent<InnateMiningScannerViewerComponent>(human), Is.False,
            "Humans should not have innate mining vision.");
        Assert.That(entMan.HasComponent<MiningScannerViewerComponent>(human), Is.False,
            "Humans should not have a mining viewer without a scanner.");
    }

    [Test]
    public async Task InnateViewerComponent_GrantsMiningViewer()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();

        var coords = new EntityCoordinates(testMap.MapUid, default);

        // A generic entity with the innate viewer component should receive the mining viewer.
        var uid = await SpawnAndWait(server, entMan, coords, "MobHuman");
        await server.WaitPost(() =>
        {
            entMan.AddComponent<InnateMiningScannerViewerComponent>(uid);
        });
        await server.WaitRunTicks(3);

        Assert.That(entMan.HasComponent<MiningScannerViewerComponent>(uid), Is.True,
            "Adding InnateMiningScannerViewerComponent should grant a MiningScannerViewerComponent.");
        Assert.That(entMan.GetComponent<MiningScannerViewerComponent>(uid).ViewRange, Is.EqualTo(5f),
            "Default innate viewer range should be used when not overridden.");
    }

    private static async Task<EntityUid> SpawnAndWait(
        Robust.UnitTesting.IServerIntegrationInstance server,
        IEntityManager entMan,
        EntityCoordinates coords,
        string prototype)
    {
        EntityUid uid = default;
        await server.WaitPost(() => uid = entMan.SpawnEntity(new ProtoId<EntityPrototype>(prototype), coords));
        await server.WaitRunTicks(3);
        return uid;
    }
}
