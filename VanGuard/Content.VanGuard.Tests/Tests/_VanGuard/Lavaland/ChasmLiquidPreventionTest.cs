#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chasm;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting;

namespace Content.VanGuard.Tests.Tests._VanGuard.Lavaland;

/// <summary>
/// Covers the Lavaland liquid behaviour on chasm tiles: liquids must not
/// pool or spread onto a chasm (<see cref="ChasmComponent"/>) — the hole
/// swallows them — while ordinary floor tiles keep spreading normally.
/// </summary>
[TestFixture]
public sealed class ChasmLiquidPreventionTest : GameTest
{
    private const string ChasmProto = "FloorChasmEntity";

    /// <summary>
    /// Spilling directly on a chasm tile does not create a puddle there.
    /// </summary>
    [Test]
    public async Task SpillOnChasmTileDoesNotPuddle()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var mapSystem = server.System<SharedMapSystem>();
        var spillSystem = server.System<PuddleSystem>();

        await server.WaitPost(() =>
        {
            var grid = map.Grid;
            // (1, 0) is a chasm tile; (0, 0) is ordinary plating.
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                    mapSystem.SetTile(grid, new Vector2i(x, y), new Tile(GetTileId(server, "Plating")));
            }
        });

        var chasmCoords = new EntityCoordinates(map.Grid, new Vector2i(1, 0));
        await server.WaitPost(() =>
        {
            SEntMan.SpawnEntity(ChasmProto, chasmCoords);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var spilled = spillSystem.TrySpillAt(
                chasmCoords,
                new Solution("Water", FixedPoint2.New(300)),
                out var puddleUid);

            Assert.That(spilled, Is.False, "A chasm tile must not accept spilled liquid.");
            Assert.That(puddleUid, Is.EqualTo(EntityUid.Invalid));
            Assert.That(HasPuddleOn(map, server, new Vector2i(1, 0)), Is.False,
                "No puddle entity may exist on the chasm tile.");
        });
    }

    /// <summary>
    /// Overflowing liquid from an ordinary tile spreads onto neighbouring
    /// floor tiles but never onto an adjacent chasm tile.
    /// </summary>
    [Test]
    public async Task OverflowSpreadsToFloorsButNotToChasm()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var mapSystem = server.System<SharedMapSystem>();
        var spillSystem = server.System<PuddleSystem>();

        await server.WaitPost(() =>
        {
            var grid = map.Grid;
            // 3x3 plating island; the chasm sits east of the centre tile.
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                    mapSystem.SetTile(grid, new Vector2i(x, y), new Tile(GetTileId(server, "Plating")));
            }

            SEntMan.SpawnEntity(ChasmProto, new EntityCoordinates(grid, new Vector2i(1, 0)));
        });

        await pair.RunTicksSync(5);

        // Centre tile gets a large volume: 300u far above the 50u overflow
        // threshold, so the spreader has to push the excess to neighbours.
        await server.WaitAssertion(() =>
        {
            var spilled = spillSystem.TrySpillAt(
                new EntityCoordinates(map.Grid, new Vector2i(0, 0)),
                new Solution("Water", FixedPoint2.New(300)),
                out _);

            Assert.That(spilled, Is.True, "Spilling on an ordinary tile must work.");
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(HasPuddleOn(map, server, new Vector2i(1, 0)), Is.False,
                "Overflowing liquid must not reach the chasm tile.");

            // The spread must still work on ordinary tiles: at least one
            // neighbouring floor tile (never the chasm one) carries liquid.
            var neighbours = new[]
            {
                new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1),
                new Vector2i(-1, 1), new Vector2i(-1, -1),
            };

            Assert.That(neighbours.Any(pos => HasPuddleOn(map, server, pos)), Is.True,
                "Overflowing liquid must still spread onto ordinary floor tiles.");
        });
    }

    private static ushort GetTileId(RobustIntegrationTest.ServerIntegrationInstance server, string tileName)
    {
        var defMan = server.ResolveDependency<ITileDefinitionManager>();
        Assert.That(defMan.TryGetDefinition(tileName, out var def), $"Unknown tile: {tileName}");
        return def!.TileId;
    }

    private static bool HasPuddleOn(
        TestMapData map,
        RobustIntegrationTest.ServerIntegrationInstance server,
        Vector2i indices)
    {
        var spillSystem = server.System<PuddleSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var grid = map.Grid;

        if (!mapSystem.TryGetTileRef(grid, grid.Comp, indices, out var tileRef))
            return false;

        return spillSystem.TryGetPuddle(tileRef, out _);
    }
}