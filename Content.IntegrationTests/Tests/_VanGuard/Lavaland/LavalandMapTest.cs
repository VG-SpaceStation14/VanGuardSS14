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
public sealed class LavalandMapTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
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
            await Server.WaitPost(() => rule = ticker.AddGameRule(loader));
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
                Assert.That(atmosphere.Pressure, Is.EqualTo(101.3f).Within(0.5f));
                Assert.That(atmosphere.GetMoles(Gas.Oxygen) / atmosphere.TotalMoles,
                    Is.EqualTo(0.14f).Within(0.01f));
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
                Is.LessThan(0.1f),
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
            Assert.That(oreCount, Is.GreaterThan(0), "No ore was generated in the sampled area.");
            Assert.That(chasmCount, Is.GreaterThan(0), "No chasms were generated in the sampled area.");
            var dungeonCount = 0;
            var gridQuery = SEntMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (gridQuery.MoveNext(out var gridUid, out _, out var gridXform))
            {
                if (gridXform.MapID == mapId && gridUid != mapUid && gridUid != outpostUid)
                    dungeonCount++;
            }
            Assert.That(dungeonCount, Is.GreaterThanOrEqualTo(2), "No Lavaland dungeon grids were loaded.");
            Assert.That(faxCount, Is.GreaterThanOrEqualTo(1), "No fax was found on Lavaland.");

            var weatherScheduler = SEntMan.GetComponent<LavalandWeatherSchedulerComponent>(mapUid);
            weatherScheduler.Stage = 2;
            weatherScheduler.NextUpdate = TimeSpan.Zero;
            await Pair.RunTicksSync(3);
            Assert.That(Server.System<SharedWeatherSystem>().HasWeather(mapId, "WeatherAshfall"), Is.True,
                "The Lavaland ash storm did not start.");

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
}
