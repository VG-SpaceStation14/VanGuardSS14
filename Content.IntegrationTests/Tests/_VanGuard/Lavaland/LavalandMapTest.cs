#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._VanGuard.Lavaland;
using Content.Server._VanGuard.Weather;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Power.Components;
using Content.Shared.Atmos;
using Content.Shared.Chasm;
using Content.Shared.Atmos.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Gravity;
using Content.Shared.Weather;
using Content.Server.Parallax;
using Content.Shared.Mining.Components;
using Content.Shared.Damage.Systems;
using Robust.Shared.Maths;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Fax.Components;
using Content.Shared.Paper;
using Content.Shared.Salvage;
using Content.Shared.Light.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Shuttles.Components;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Lavaland;

[TestFixture]
[NonParallelizable]
public sealed class LavalandMapTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    [Test]
    public async Task LavalandPlanetOutpostAndFtlAreCreated()
    {
        var ticker = Server.System<ServerGameTicker>();
        Entity<GameRuleComponent>? rule = null;
        EntityUid? player = null;

        try
        {
            var loader = new EntProtoId("LavalandLoader");
            await Server.WaitPost(() =>
            {
                rule = ticker.AddGameRule(loader);
                if (rule is { } added)
                    SEntMan.GetComponent<LavalandOutpostRuleComponent>(added.Owner).Seed = 424242;
            });
            await Pair.RunUntilSynced();

            Assert.That(rule, Is.Not.Null, "LavalandLoader rule was not added.");

            var ruleUid = rule!.Value.Owner;
            var ruleComp = SEntMan.GetComponent<LavalandOutpostRuleComponent>(ruleUid);

            Assert.Multiple(() =>
            {
                Assert.That(ruleComp.Map, Is.Not.Null, "The rule did not create a map.");
                Assert.That(ruleComp.MapId, Is.Not.Null, "The rule did not record a map id.");
                Assert.That(ruleComp.OutpostGrid, Is.Not.Null, "The outpost grid was not loaded.");
            });

            var mapUid = ruleComp.Map!.Value;
            var mapId = ruleComp.MapId!.Value;
            var outpostUid = ruleComp.OutpostGrid!.Value;

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<BiomeComponent>(mapUid));
                Assert.That(SEntMan.HasComponent<MapGridComponent>(mapUid));
                Assert.That(SEntMan.HasComponent<GravityComponent>(mapUid));
                Assert.That(SEntMan.HasComponent<MapLightComponent>(mapUid));
                Assert.That(SEntMan.HasComponent<MapAtmosphereComponent>(mapUid));
                Assert.That(SEntMan.HasComponent<FTLDestinationComponent>(mapUid));
            Assert.That(SEntMan.HasComponent<RestrictedRangeComponent>(mapUid), Is.True);
            Assert.That(SEntMan.GetComponent<RestrictedRangeComponent>(mapUid).BoundaryEntity, Is.Not.EqualTo(EntityUid.Invalid));
                Assert.That(SEntMan.HasComponent<MapGridComponent>(outpostUid));
                Assert.That(SEntMan.GetComponent<TransformComponent>(outpostUid).MapID, Is.EqualTo(mapId));
                Assert.That(SEntMan.GetComponent<BiomeComponent>(mapUid).Template?.Id, Is.EqualTo("Lava"));
                Assert.That(SEntMan.GetComponent<BiomeComponent>(mapUid).Seed, Is.Not.EqualTo(-1));
            });

            var biomeMarkers = SEntMan.GetComponent<BiomeComponent>(mapUid).MarkerLayers;
            Assert.Multiple(() =>
            {
                Assert.That(biomeMarkers, Does.Contain("OreCoal"));
                Assert.That(biomeMarkers, Does.Contain("LavalandFeatures"));
            });

            var atmosphereSystem = SEntMan.System<AtmosphereSystem>();
            var mapAtmosphere = new Entity<MapAtmosphereComponent?>(mapUid, null);
            var atmosphere = atmosphereSystem.GetTileMixture(null, mapAtmosphere, Vector2i.Zero);
            Assert.Multiple(() =>
            {
                Assert.That(atmosphere, Is.Not.Null);
                Assert.That(atmosphere!.Temperature, Is.EqualTo(313.15f).Within(0.1f));
                Assert.That(atmosphere.Pressure, Is.EqualTo(38.4f).Within(0.5f));
                Assert.That(atmosphere.GetMoles(Gas.Oxygen), Is.EqualTo(13.65346f).Within(0.05f));
                Assert.That(atmosphere.GetMoles(Gas.Nitrogen), Is.EqualTo(23.24754f).Within(0.05f));
            });

            var lightCycle = SEntMan.GetComponent<LightCycleComponent>(mapUid);
            Assert.That(lightCycle.Enabled, Is.False);

            Assert.That(SEntMan.HasComponent<RoofComponent>(mapUid), Is.False);

            var sunShadow = SEntMan.GetComponent<SunShadowComponent>(mapUid);
            Assert.Multiple(() =>
            {
                Assert.That(sunShadow.Direction, Is.EqualTo(new Vector2(-1f, -1f)));
                Assert.That(sunShadow.Alpha, Is.EqualTo(0.75f).Within(0.001f));
            });

            var gravity = SEntMan.GetComponent<GravityComponent>(mapUid);
            Assert.Multiple(() =>
            {
                Assert.That(gravity.Enabled);
                Assert.That(gravity.Inherent);
            });

            var destination = SEntMan.GetComponent<FTLDestinationComponent>(mapUid);
            Assert.Multiple(() =>
            {
                Assert.That(destination.Enabled);
                Assert.That(destination.BeaconsOnly, Is.False);
                Assert.That(destination.RequireCoordinateDisk, Is.False);
                Assert.That(destination.Whitelist, Is.Null);
            });

            EntityUid? ftlPoint = null;
            var beacons = SEntMan.EntityQueryEnumerator<FTLBeaconComponent, TransformComponent>();
            while (beacons.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != outpostUid)
                    continue;

                ftlPoint = uid;
                break;
            }

            Assert.That(ftlPoint, Is.Not.Null, "No FTLPoint was spawned at the outpost.");
            var localization = Server.ResolveDependency<ILocalizationManager>();
            Assert.That(
                SEntMan.GetComponent<MetaDataComponent>(ftlPoint!.Value).EntityName,
                Is.EqualTo(localization.GetString("lavaland-ftl-point")));

            var statsBiome = SEntMan.GetComponent<BiomeComponent>(mapUid);
            var outpostGrid = SEntMan.GetComponent<MapGridComponent>(outpostUid);
            var outpostXform = SEntMan.GetComponent<TransformComponent>(outpostUid);

            Assert.That(
                (outpostXform.LocalPosition + outpostGrid.LocalAABB.Center).Length(),
                Is.LessThan(1f),
                "The outpost grid was not centred on the map.");

            var matrix = Matrix3x2.CreateRotation((float) outpostXform.LocalRotation) *
                         Matrix3x2.CreateTranslation(outpostXform.LocalPosition);
            var clearBounds = matrix.TransformBox(outpostGrid.LocalAABB.Enlarged(ruleComp.ClearRadius));

            var modified = new HashSet<Vector2i>();
            foreach (var set in statsBiome.ModifiedTiles.Values)
                modified.UnionWith(set);

            for (var x = (int) clearBounds.Left; x < (int) clearBounds.Right; x++)
            {
                for (var y = (int) clearBounds.Bottom; y < (int) clearBounds.Top; y++)
                {
                    Assert.That(modified.Contains(new Vector2i(x, y)),
                        $"Tile {x},{y} in the outpost clearance was not reserved.");
                }
            }

            var mapGrid = SEntMan.GetComponent<MapGridComponent>(mapUid);
            var mapSystem = SEntMan.System<SharedMapSystem>();
            var centerTile = new Vector2i((int) clearBounds.Center.X, (int) clearBounds.Center.Y);
            Assert.That(mapSystem.TryGetTile(mapGrid, centerTile, out var beforeTile));
            Assert.That(beforeTile.IsEmpty, Is.False);

            // Attach a viewer so BiomeSystem.Update actually loads chunks around the
            // outpost, then verify that nothing from the biome was spawned in the clear zone.
            await Server.WaitPost(() =>
            {
                player = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(mapUid, Vector2.Zero));
                Assert.That(ServerSession, Is.Not.Null);
                Server.PlayerMan.SetAttachedEntity(ServerSession, player.Value);
            });
            await Pair.RunTicksSync(15);

            foreach (var (_, entities) in statsBiome.LoadedEntities)
            {
                foreach (var (entity, tile) in entities)
                {
                    if (!SEntMan.EntityExists(entity))
                        continue;

                    Assert.That(
                        clearBounds.Contains(new Vector2(tile.X + 0.5f, tile.Y + 0.5f)),
                        Is.False,
                        $"Biome entity {SEntMan.ToPrettyString(entity)} spawned inside the outpost clearance.");
                }
            }

            Assert.That(mapSystem.TryGetTile(mapGrid, centerTile, out var afterTile));
            Assert.That(afterTile, Is.EqualTo(beforeTile),
                "A reserved tile was regenerated after biome chunks loaded.");

            var poiCount = 0;
            var faxCount = 0;
            var poiQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (poiQuery.MoveNext(out _, out var poiMeta, out var poiXform))
            {
                if (poiXform.MapID == mapId && poiMeta.EntityPrototype?.ID.StartsWith("LavalandTendril") == true)
                    poiCount++;
            }
            var faxQuery = SEntMan.EntityQueryEnumerator<FaxMachineComponent, TransformComponent>();
            while (faxQuery.MoveNext(out var faxUid, out _, out var faxXform))
            {
                if (faxXform.MapID != mapId)
                    continue;

                faxCount++;
                if (SEntMan.TryGetComponent<ApcPowerReceiverComponent>(faxUid, out var faxPower))
                {
                    faxPower.NeedsPower = false;
                    faxPower.Powered = true;
                }
            }
            Assert.That(poiCount, Is.GreaterThanOrEqualTo(1), "No POIs were spawned.");
            Server.System<BiomeSystem>().Preload(mapUid, statsBiome, new Box2(-160, -160, 160, 160));
            await Pair.RunTicksSync(60);

            // Every tendril, from the POI spawner and from the biome feature
            // layer, must stand on a real floor tile that is neither lava nor a
            // chasm. This is checked after the biome has actually generated the
            // terrain underneath the POIs.
            var lookup = SEntMan.System<EntityLookupSystem>();
            var tendrilMapSystem = SEntMan.System<SharedMapSystem>();
            var mapGridComp = SEntMan.GetComponent<MapGridComponent>(mapUid);
            var tendrilQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (tendrilQuery.MoveNext(out var tendril, out var tendrilMeta, out var tendrilXform))
            {
                if (tendrilXform.MapID != mapId ||
                    tendrilMeta.EntityPrototype?.ID.StartsWith("LavalandTendril") != true)
                {
                    continue;
                }

                var indices = tendrilMapSystem.TileIndicesFor(mapUid, mapGridComp, tendrilXform.Coordinates);
                Assert.That(tendrilMapSystem.TryGetTile(mapGridComp, indices, out var tendrilTile) && !tendrilTile.IsEmpty,
                    Is.True, $"Tendril {tendrilMeta.EntityPrototype.ID} at {indices} is not on a floor tile.");
                Assert.That(tendrilXform.Anchored, Is.True,
                    $"Tendril {tendrilMeta.EntityPrototype.ID} at {indices} is not anchored to its grid.");
                Assert.That(tendrilXform.GridUid, Is.EqualTo(mapGridComp.Owner),
                    $"Tendril {tendrilMeta.EntityPrototype.ID} at {indices} is not on the planet grid.");

                foreach (var nearby in lookup.GetEntitiesInRange(tendrilXform.Coordinates, 0.5f))
                {
                    if (nearby == tendril)
                        continue;

                    // A dungeon grid may overlap the surface in world space;
                    // only terrain on the tendril's own grid matters.
                    if (SEntMan.GetComponent<TransformComponent>(nearby).GridUid != tendrilXform.GridUid)
                        continue;

                    Assert.That(SEntMan.HasComponent<Content.Shared.Chasm.ChasmComponent>(nearby), Is.False,
                        $"Tendril {tendrilMeta.EntityPrototype.ID} at {indices} was placed on a chasm.");
                    Assert.That(SEntMan.GetComponent<MetaDataComponent>(nearby).EntityPrototype?.ID,
                        Is.Not.EqualTo("FloorLavaEntity"),
                        $"Tendril {tendrilMeta.EntityPrototype.ID} at {indices} was placed on lava.");
                }
            }

            var oreCount = 0;
            var chasmCount = 0;
            var rockCount = 0;
            var oreQuery = SEntMan.EntityQueryEnumerator<OreVeinComponent, TransformComponent>();
            while (oreQuery.MoveNext(out _, out _, out var oreXform))
            {
                if (oreXform.MapID == mapId)
                    oreCount++;
            }

            var chasmQuery = SEntMan.EntityQueryEnumerator<Content.Shared.Chasm.ChasmComponent, TransformComponent>();
            while (chasmQuery.MoveNext(out _, out _, out var chasmXform))
            {
                if (chasmXform.MapID == mapId)
                    chasmCount++;
            }

            var rockQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (rockQuery.MoveNext(out _, out var rockMeta, out var rockXform))
            {
                if (rockXform.MapID == mapId &&
                    (rockMeta.EntityPrototype?.ID.StartsWith("AsteroidRock") == true ||
                     rockMeta.EntityPrototype?.ID.StartsWith("WallRock") == true))
                {
                    rockCount++;
                }
            }

            TestContext.Progress.WriteLine($"Lavaland generation stats: ore={oreCount}, chasms={chasmCount}, rocks={rockCount}");
            var chasmSizes = ChasmComponentSizes(SEntMan, mapId);
            var largestChasm = chasmSizes.Count == 0 ? 0 : chasmSizes[^1];
            var singles = chasmSizes.Count(size => size == 1);
            TestContext.Progress.WriteLine($"Lavaland chasm components: total={chasmCount}, areas={chasmSizes.Count}, largest={largestChasm}, singles={singles}, sizes={string.Join(',', chasmSizes.TakeLast(10))}");
            Assert.That(chasmSizes.Count, Is.GreaterThan(0));
            // The planet must still have a connected chasm field, but chasms
            // must not overwhelm the surface: the user explicitly asked for
            // fewer, navigable chasm fields.
            Assert.That(largestChasm, Is.GreaterThanOrEqualTo(3), "No connected chasm field was generated.");
            Assert.That(chasmCount, Is.LessThan(512), "Too many chasm tiles were generated; Lavaland is not traversable.");
            Assert.That(oreCount, Is.GreaterThan(0), "No ore was generated in the sampled area.");
            Assert.That(chasmCount, Is.GreaterThan(0), "No chasms were generated in the sampled area.");
            var dungeonCount = 0;
            var gridQuery = SEntMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (gridQuery.MoveNext(out var gridUid, out _, out var gridXform))
            {
                if (gridXform.MapID == mapId && gridUid != mapUid && gridUid != outpostUid)
                    dungeonCount++;
            }
            Assert.That(dungeonCount, Is.EqualTo(10), "Expected eight dungeons, the Blood Miner arena and the Legion base.");

            // Exactly one Blood Miner arena must be generated on the planet.
            var arenaName = localization.GetString("lavaland-poi-blood-miner-arena");
            EntityUid? arenaGrid = null;
            var arenaCount = 0;
            var arenaQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (arenaQuery.MoveNext(out var arenaUid, out var arenaMeta, out var arenaXform))
            {
                if (arenaXform.MapID != mapId || arenaMeta.EntityName != arenaName)
                    continue;

                arenaCount++;
                arenaGrid = arenaUid;
            }
            Assert.That(arenaCount, Is.EqualTo(1), "Exactly one Blood Miner arena must be generated.");

            var legionBaseName = localization.GetString("lavaland-poi-legion-base");
            EntityUid? legionBaseGrid = null;
            var legionBaseCount = 0;
            var legionBaseQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (legionBaseQuery.MoveNext(out var legionUid, out var legionMeta, out var legionXform))
            {
                if (legionXform.MapID != mapId || legionMeta.EntityName != legionBaseName)
                    continue;

                legionBaseCount++;
                legionBaseGrid = legionUid;
            }
            Assert.That(legionBaseCount, Is.EqualTo(1), "Exactly one Legion base must be generated.");
            Assert.That(legionBaseGrid, Is.Not.Null);

            var legionGridComp = SEntMan.GetComponent<MapGridComponent>(legionBaseGrid!.Value);
            var legionWorldBounds = legionGridComp.LocalAABB.Translated(
                SEntMan.System<SharedTransformSystem>().GetWorldPosition(legionBaseGrid.Value));
            var legionRockCount = 0;
            var legionRockQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (legionRockQuery.MoveNext(out _, out var legionRockMeta, out var legionRockXform))
            {
                if (legionRockXform.MapID != mapId || legionRockXform.GridUid != mapUid)
                    continue;

                var id = legionRockMeta.EntityPrototype?.ID;
                if (id?.StartsWith("AsteroidRock", StringComparison.Ordinal) != true &&
                    id?.StartsWith("WallRock", StringComparison.Ordinal) != true)
                    continue;

                if (legionWorldBounds.Contains(legionRockXform.WorldPosition))
                    legionRockCount++;
            }
            Assert.That(legionRockCount, Is.Zero, "Rocks remain inside the Legion base footprint.");

            var legionSpawnerCount = 0;
            var legionSpawnerQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (legionSpawnerQuery.MoveNext(out _, out var legionSpawnerMeta, out var legionSpawnerXform))
            {
                if (legionSpawnerXform.MapID == mapId && legionSpawnerMeta.EntityPrototype?.ID == "LavalandSpawnMobLegion")
                    legionSpawnerCount++;
            }
            Assert.That(legionSpawnerCount, Is.Zero, "The Legion base spawner was not consumed.");

            var bossCount = 0;
            var bossQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (bossQuery.MoveNext(out _, out var bossMeta, out var bossXform))
            {
                if (bossXform.MapID == mapId && bossMeta.EntityPrototype?.ID == "LavalandMobBloodMiner")
                    bossCount++;
            }
            Assert.That(bossCount, Is.EqualTo(1),
                $"The Blood Miner arena did not spawn exactly one phase-one boss (found {bossCount}, arenas {arenaCount}, grids {dungeonCount}).");

            var legionCount = 0;
            var staffCount = 0;
            var uniqueContentQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (uniqueContentQuery.MoveNext(out _, out var uniqueMeta, out var uniqueXform))
            {
                if (uniqueXform.MapID != mapId)
                    continue;
                if (uniqueMeta.EntityPrototype?.ID == "LavalandMobLegion")
                    legionCount++;
                if (uniqueMeta.EntityPrototype?.ID == "LavalandWeaponStaffHealing")
                    staffCount++;
            }
            Assert.That(legionCount, Is.GreaterThanOrEqualTo(1), "No legion spawned in the Legion base.");
            if (staffCount != 1)
            {
                var staffDebug = new List<string>();
                var debugQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (debugQuery.MoveNext(out var debugUid, out var debugMeta, out var debugXform))
                {
                    if (debugMeta.EntityPrototype?.ID == "LavalandWeaponStaffHealing")
                        staffDebug.Add($"{debugUid} map={debugXform.MapID} grid={debugXform.GridUid} parent={debugXform.ParentUid}");
                }
                Assert.Fail($"The Legion base did not spawn its healing staff (count={staffCount}, all={string.Join("; ", staffDebug)}).");
            }

            var bossSpawnerCount = 0;
            var spawnerQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (spawnerQuery.MoveNext(out _, out var spawnerMeta, out var spawnerXform))
            {
                if (spawnerXform.MapID == mapId && spawnerMeta.EntityPrototype?.ID == "LavalandSpawnMobBloodMiner")
                    bossSpawnerCount++;
            }
            Assert.That(bossSpawnerCount, Is.Zero, "The arena boss spawner was not consumed.");

            Assert.That(arenaGrid, Is.Not.Null);
            var xformSystem = SEntMan.System<SharedTransformSystem>();
            var arenaDistance = (xformSystem.GetWorldPosition(arenaGrid!.Value) - xformSystem.GetWorldPosition(outpostUid)).Length();
            Assert.That(arenaDistance, Is.InRange(90f, 230f), "The arena was not placed within the configured player-reach ring.");

            var dungeonGrids = new List<(EntityUid Uid, Angle Rotation)>();
            var stableQuery = SEntMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (stableQuery.MoveNext(out var stableUid, out _, out var stableXform))
            {
                if (stableXform.MapID != mapId || stableUid == mapUid || stableUid == outpostUid)
                    continue;
                dungeonGrids.Add((stableUid, stableXform.LocalRotation));
                Assert.That(SEntMan.HasComponent<Content.Shared.Shuttles.Components.ShuttleComponent>(stableUid), Is.False,
                    $"Dungeon grid {stableUid} still has Shuttle physics.");
            }

            for (var i = 0; i < dungeonGrids.Count; i++)
            {
                var first = dungeonGrids[i].Uid;
                var firstGrid = SEntMan.GetComponent<MapGridComponent>(first);
                var firstBounds = firstGrid.LocalAABB.Translated(xformSystem.GetWorldPosition(first));
                for (var j = i + 1; j < dungeonGrids.Count; j++)
                {
                    var second = dungeonGrids[j].Uid;
                    var secondGrid = SEntMan.GetComponent<MapGridComponent>(second);
                    var secondBounds = secondGrid.LocalAABB.Translated(xformSystem.GetWorldPosition(second));
                    Assert.That(firstBounds.Intersects(secondBounds), Is.False,
                        $"Dungeon grids {first} {firstBounds} and {second} {secondBounds} overlap.");
                }
            }

            await Pair.RunTicksSync(120);
            await Server.WaitPost(() =>
            {
                foreach (var (uid, rotation) in dungeonGrids)
                {
                    var xform = SEntMan.GetComponent<TransformComponent>(uid);
                    Assert.That(xform.LocalRotation, Is.EqualTo(rotation),
                        $"Dungeon grid {uid} rotated after initialization.");
                }
            });
            Assert.That(faxCount, Is.GreaterThanOrEqualTo(1), "No fax was found on Lavaland.");

            var faxText = string.Empty;
            var paperQuery = SEntMan.EntityQueryEnumerator<PaperComponent, TransformComponent>();
            while (paperQuery.MoveNext(out _, out var paper, out var paperXform))
            {
                if (paperXform.MapID == mapId)
                    faxText += paper.Content;
            }

            var expectedTendrils = 0;
            var poiSpawnerQuery = SEntMan.EntityQueryEnumerator<LavalandPoiSpawnerComponent>();
            if (poiSpawnerQuery.MoveNext(out _, out var poiSpawner))
                expectedTendrils = poiSpawner.Prototypes.Count;

            var faxLines = faxText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.Contains("X=", StringComparison.Ordinal))
                .ToList();
            Assert.That(faxLines, Has.Count.EqualTo(dungeonCount + expectedTendrils),
                "The POI fax must contain one coordinate for every dungeon, arena, base and configured tendril.");
            foreach (var line in faxLines)
                Assert.That(line, Does.Match(@"X=-?\d+, Y=-?\d+"),
                    $"POI fax coordinate is not rounded to integers: {line}");

            Assert.That(faxText, Does.Contain(localization.GetString("lavaland-fax-object-arena")),
                "The POI fax is missing the Blood Miner arena.");
            Assert.That(faxText, Does.Contain(localization.GetString("lavaland-fax-object-legion-base")),
                "The POI fax is missing the Legion base.");
            foreach (var dungeonLabel in new[]
                     {
                         "lavaland-fax-object-monument",
                         "lavaland-fax-object-lake-village",
                         "lavaland-fax-object-cave",
                         "lavaland-fax-object-bright-room",
                         "lavaland-fax-object-green-dome",
                         "lavaland-fax-object-lava-river",
                         "lavaland-fax-object-mineshaft",
                         "lavaland-fax-object-cold-dome",
                     })
            {
                Assert.That(faxText, Does.Contain(localization.GetString(dungeonLabel)),
                    $"The POI fax is missing dungeon label {dungeonLabel}.");
            }
            Assert.That(faxText, Does.Not.Contain(localization.GetString("lavaland-dungeon-temple")),
                "The POI fax must use vague survey labels, not exact dungeon names.");

            var weatherScheduler = SEntMan.GetComponent<LavalandWeatherSchedulerComponent>(mapUid);
            weatherScheduler.Stage = 2;
            weatherScheduler.NextUpdate = TimeSpan.Zero;
            await Pair.RunTicksSync(3);
            Assert.That(Server.System<SharedWeatherSystem>().HasWeather(mapId, "WeatherAshfall"), Is.True,
                "The Lavaland ash storm did not start.");

            // The map atmosphere must actually affect a player standing on the
            // bare surface, not merely exist as data on the map component.
            EntityUid sufferer = default;
            await Server.WaitPost(() =>
            {
                var surface = new Vector2i(60, 60);
                var tileMan = Server.ResolveDependency<ITileDefinitionManager>();
                var basalt = new Tile(tileMan["FloorBasalt"].TileId);
                Server.System<LavalandFeatureSystem>().PrepareSpawnTile(mapUid, mapGrid, surface, basalt);
                var coords = Server.System<SharedMapSystem>().GridTileToLocal(mapUid, mapGrid, surface);
                sufferer = SEntMan.SpawnEntity("MobHuman", coords);
            });
            await Pair.RunSeconds(10f);
            await Server.WaitPost(() =>
            {
                var damage = Server.System<DamageableSystem>().GetTotalDamage(sufferer).Float();
                Assert.That(damage, Is.GreaterThan(0f),
                    "A player on the oxygen-free Lavaland surface took no asphyxiation damage.");
                SEntMan.DeleteEntity(sufferer);
            });

        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (player is { } p && SEntMan.EntityExists(p))
                    SEntMan.DeleteEntity(p);

                if (ServerSession != null)
                    Server.PlayerMan.SetAttachedEntity(ServerSession, null);

                if (rule is { } r && SEntMan.EntityExists(r.Owner))
                    ticker.EndGameRule((r.Owner, (GameRuleComponent?) r.Comp));

                if (rule != null &&
                    SEntMan.TryGetComponent<LavalandOutpostRuleComponent>(rule.Value.Owner, out var component) &&
                    component.Map is { } map &&
                    SEntMan.EntityExists(map))
                {
                    SEntMan.DeleteEntity(map);
                }
            });
            await Pair.RunUntilSynced();
        }
    }

    private static List<int> ChasmComponentSizes(Robust.Shared.GameObjects.IEntityManager entMan, MapId mapId)
    {
        var positions = new List<Vector2>();
        var query = entMan.EntityQueryEnumerator<Content.Shared.Chasm.ChasmComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapID == mapId)
                positions.Add(xform.LocalPosition);
        }

        var seen = new HashSet<Vector2>();
        var sizes = new List<int>();
        foreach (var start in positions)
        {
            if (!seen.Add(start))
                continue;
            var queue = new Queue<Vector2>();
            queue.Enqueue(start);
            var size = 0;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                size++;
                foreach (var other in positions)
                {
                    if (seen.Contains(other) || (other - current).LengthSquared() > 1.5f * 1.5f)
                        continue;
                    seen.Add(other);
                    queue.Enqueue(other);
                }
            }
            sizes.Add(size);
        }

        sizes.Sort();
        return sizes;
    }
}
