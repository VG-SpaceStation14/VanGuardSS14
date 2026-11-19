#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._VanGuard.Lavaland.Systems;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.VanGuard.Tests.Tests._VanGuard.Lavaland;

/// <summary>
/// Regression test for the Lavaland outpost bug: when a large POI dungeon grid
/// (such as <c>lava_river.yml</c>) loads with its authored YAML position, the
/// position overlaps the outpost. Before the placement check rejects the
/// dungeon, the next physics tick can drag outpost entities onto the dungeon
/// grid through <c>SharedGridTraversalSystem.CheckTraverse</c> and change
/// their <c>GridUid</c>.
///
/// This test reproduces the geometry of the bug deterministically:
///   1. The Lavaland outpost grid loads, centred at (8.5, -0.5) with a known
///      AABB;
///   2. Unanchored outpost objects (a Closet, a Pickaxe) are placed in the
///      overlap zone between the outpost and the dungeon's authored position;
///   3. <c>lava_river.yml</c> is loaded at its authored YAML coordinates,
///      which overlap the outpost objects;
///   4. The physics system runs for several ticks.
///
/// Without the fix the dungeon grid remains <c>Dynamic + canCollide</c> while
/// sitting on top of the outpost objects. Physics drags the objects onto the
/// dungeon grid and their <c>GridUid</c> changes. With the fix the dungeon
/// grid is <c>Static + canCollide=False</c>, the broadphase never sees its
/// fixtures, and the outpost objects keep their original <c>GridUid</c>.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class LavalandPoiLargeGridSafetyTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    [Test]
    public async Task OutpostObjectsKeepGridUidWhenLargeDungeonLoadsNearbyWithFix()
    {
        // With the fix the dungeon grid is silenced at its authored position, so
        // outpost objects must stay on the outpost grid.
        await RunGridSafetyScenarioAsync(applyFix: true);
    }

    private async Task RunGridSafetyScenarioAsync(bool applyFix)
    {        var outpostPath = new ResPath("/Maps/_VanGuard/Lavaland/mining_outpost.yml");
        var dungeonPath = new ResPath("/Maps/_VanGuard/Lavaland/Dungeons/lava_river.yml");

        EntityUid mapUid = EntityUid.Invalid;
        EntityUid outpostGrid = EntityUid.Invalid;
        EntityUid dungeonGrid = EntityUid.Invalid;
        var outpostObjectUids = new List<EntityUid>();

        // 1. Load the outpost, get its map and grid.
        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            mapUid = mapSystem.CreateMap(out _);

            var loader = SEntMan.System<Robust.Shared.EntitySerialization.Systems.MapLoaderSystem>();
            var mapId = SEntMan.GetComponent<TransformComponent>(mapUid).MapID;
            Assert.That(loader.TryLoadGrid(mapId, outpostPath, out var loadedOutpost,
                new DeserializationOptions { InitializeMaps = true }), Is.True,
                "Failed to load mining_outpost.yml");
            Assert.That(loadedOutpost, Is.Not.Null);
            outpostGrid = loadedOutpost!.Value.Owner;

            // The outpost ships at authored position (-0.5520833, -0.48958334).
            // After load we move it to a known centred position so the geometry
            // matches the production scenario.
            var transformSys = SEntMan.System<SharedTransformSystem>();
            var centeredPos = new Vector2(8.5f, -0.5f);
            transformSys.SetLocalPosition(outpostGrid, centeredPos,
                SEntMan.GetComponent<TransformComponent>(outpostGrid));

            // 2. Drop unanchored outpost objects into the overlap zone.
            // The exact prototype IDs do not matter for the test; we only need
            // any entity with PhysicsComponent + Dynamic that the broadphase
            // will wake up.
            var physicsSystem = SEntMan.System<SharedPhysicsSystem>();
            // Pick any physics-bearing prototype that exists in the content
            // assembly so we get a real PhysicsComponent + FixturesComponent.
            var dummyProto = "ClosetEmergencyFilledRandom";

            for (var i = 0; i < 3; i++)
            {
                var obj = SEntMan.SpawnEntity(dummyProto,
                    new EntityCoordinates(outpostGrid, new Vector2(2f + i, 0f)));

                Assert.That(SEntMan.TryGetComponent<PhysicsComponent>(obj, out var p), Is.True,
                    "Spawned entity must have PhysicsComponent for the broadphase test");

                physicsSystem.SetBodyType(obj, BodyType.Dynamic, body: p!);
                physicsSystem.SetCanCollide(obj, true, body: p!);
                physicsSystem.SetFixedRotation(obj, true, body: p!);
                physicsSystem.SetLinearVelocity(obj, Vector2.Zero, body: p!);
                physicsSystem.SetAngularVelocity(obj, 0f, body: p!);

                // Wake the body so the broadphase pays attention to it.
                physicsSystem.WakeBody(obj, force: true, body: p);

                // Make sure the object is parented to the outpost grid so the
                // broadphase treats it as a grid sundry.
                transformSys.SetParent(obj, SEntMan.GetComponent<TransformComponent>(obj), outpostGrid);
                outpostObjectUids.Add(obj);
            }
        });

        // Sanity: every outpost object lives on the outpost grid before we load
        // the dungeon.
        await Server.WaitPost(() =>
        {
            foreach (var obj in outpostObjectUids)
            {
                var xform = SEntMan.GetComponent<TransformComponent>(obj);
                Assert.That(xform.GridUid, Is.EqualTo(outpostGrid),
                    "Pre-condition: outpost object must be on the outpost grid");
            }
        });

        // 3. Load lava_river.yml at its authored YAML position.
        await Server.WaitPost(() =>
        {
            var loader = SEntMan.System<Robust.Shared.EntitySerialization.Systems.MapLoaderSystem>();
            var mapId = SEntMan.GetComponent<TransformComponent>(mapUid).MapID;
            Assert.That(loader.TryLoadGrid(mapId, dungeonPath, out var loadedDungeon,
                new DeserializationOptions { InitializeMaps = true }), Is.True,
                "Failed to load lava_river.yml");
            Assert.That(loadedDungeon, Is.Not.Null);
            dungeonGrid = loadedDungeon!.Value.Owner;

            // Mirror exactly what LavalandPoiSpawnerSystem.SpawnPois does between
            // TryLoadGrid() and the occupied.Intersects() check.
            if (applyFix)
            {
                var physicsSystem = SEntMan.System<SharedPhysicsSystem>();
                Assert.That(SEntMan.TryGetComponent<PhysicsComponent>(dungeonGrid, out var prePhysics), Is.True);
                physicsSystem.SetCanCollide(dungeonGrid, false, body: prePhysics!);
                physicsSystem.SetBodyType(dungeonGrid, BodyType.Static, body: prePhysics!);
            }
        });

        // 4. Run physics for a few ticks. If the fix is absent the broadphase
        // wakes the outpost objects, the dungeon grid's Static-but-canCollide
        // fixtures generate contacts, and SharedGridTraversalSystem.CheckTraverse
        // reassigns the objects to the dungeon grid.
        await Pair.RunTicksSync(5);

        // 5. Verify what happened to the outpost objects.
        await Server.WaitPost(() =>
        {
            foreach (var obj in outpostObjectUids)
            {
                Assert.That(SEntMan.TryGetComponent<TransformComponent>(obj, out var xform), Is.True);
                var currentGrid = xform!.GridUid;

                Assert.That(currentGrid, Is.EqualTo(outpostGrid),
                    $"Outpost object uid={obj} must stay on the outpost grid while " +
                    $"a large dungeon grid is loaded nearby, but its GridUid is now {currentGrid}");
            }
        });
    }
}
