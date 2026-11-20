using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._VanGuard.Lavaland.Components;
using Content.Server._VanGuard.Lavaland.Systems;
using Content.Server.Parallax;
using Content.Shared.Light.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Tests._VanGuard.Lavaland;

/// <summary>
/// Replays the Lavaland production chain step by step and records the state of the
/// outpost entities after every single stage, so the exact stage that loses them
/// can be identified.
/// </summary>
[TestFixture]
public sealed class LavalandProductionChainTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private const string OutpostPath = "/Maps/_VanGuard/Lavaland/mining_outpost.yml";

    private static readonly (string Id, Vector2 Pos)[] Targets =
    {
        ("LockerMinerFilled", new Vector2(-23.5f, -4.5f)),
        ("LockerMinerFilled", new Vector2(-24.5f, -4.5f)),
        ("LockerMinerFilled", new Vector2(-25.5f, -4.5f)),
        ("LockerMinerFilled", new Vector2(-26.5f, -4.5f)),
        ("LockerMinerFilled", new Vector2(-27.5f, -4.5f)),
        ("ClosetEmergencyFilledRandom", new Vector2(-19.5f, -11.5f)),
        ("ClosetEmergencyFilledRandom", new Vector2(11.5f, 9.5f)),
        ("OxygenCanister", new Vector2(-14.5f, 5.5f)),
        ("Pickaxe", new Vector2(-17.495617f, -12.417178f)),
        ("Pickaxe", new Vector2(-17.526867f, -12.526553f)),
        ("Pickaxe", new Vector2(12.4728f, 11.584418f)),
        ("Pickaxe", new Vector2(12.613425f, 11.412543f)),
        ("SpaceHeater", new Vector2(7.5f, 7.5f)),
    };

    [Test]
    [Retry(3)]
    public async Task ReplayProductionChainAndRecordOutpostState()
    {
        var report = new System.Text.StringBuilder();

        foreach (var seed in new[] { 424242, 1, 2, 3, 7, 13, 100, 4243 })
        {
            report.AppendLine($"################ SEED {seed} ################");

            await Server.WaitPost(() =>
            {
                var mapSystem = SEntMan.System<SharedMapSystem>();
                var loader = SEntMan.System<MapLoaderSystem>();
                var biome = SEntMan.System<BiomeSystem>();
                var poi = SEntMan.System<LavalandPoiSpawnerSystem>();
                var transform = SEntMan.System<SharedTransformSystem>();

                // The planet map is created and map-initialized before the rule runs,
                // exactly like LoadMapRuleSystem does.
                mapSystem.CreateMap(out var mapId, runMapInit: true);
                mapSystem.TryGetMap(mapId, out var mapEntity);
                var mapUid = mapEntity!.Value;
                _lastMapId = mapId;

                var ruleComp = new LavalandOutpostRuleComponent
                {
                    Seed = seed,
                    MarkerLayers = new List<string>
                    {
                        "LavalandChasm",
                        "LavalandChasmOpen",
                        "LavalandArmokPit",
                        "LavalandFeatures",
                        "LavalandRareMobs",
                        "OreIron",
                        "OreCoal",
                        "OreGold",
                        "OreSilver",
                        "OrePlasma",
                        "OreUranium",
                        "OreQuartz",
                        "OreDiamond",
                    },
                };

                var protoMan = Server.ResolveDependency<IPrototypeManager>();

                // ---- Stage 1: EnsurePlanet.
                biome.EnsurePlanet(mapUid, protoMan.Index(ruleComp.Biome), ruleComp.Seed, mapLight: ruleComp.MapLight);
                Snapshot(report, "1 EnsurePlanet", mapId);

                // ---- Stage 2: ApplyPlanetOverrides.
                ApplyPlanetOverrides(report, mapUid, ruleComp);

                // ---- Stage 3: ApplyMarkerLayers.
                foreach (var marker in ruleComp.MarkerLayers)
                    biome.AddMarkerLayer(mapUid, SEntMan.GetComponent<BiomeComponent>(mapUid), marker);
                Snapshot(report, "3 ApplyMarkerLayers", mapId);

                // ---- Stage 4: TryLoadGrid for the outpost.
                if (!loader.TryLoadGrid(
                        mapId,
                        ruleComp.GridPath,
                        out var loadedGrid,
                        new DeserializationOptions { InitializeMaps = true }))
                {
                    report.AppendLine("  !! TryLoadGrid FAILED");
                    return;
                }

                var outpost = loadedGrid!.Value;
                Snapshot(report, "4 TryLoadGrid(outpost)", mapId);
                SnapshotOutpost(report, "4 TryLoadGrid(outpost)", outpost.Owner);

                // ---- Centre the outpost, exactly like the rule does.
                var outpostXform = SEntMan.GetComponent<TransformComponent>(outpost.Owner);
                var correction = outpostXform.LocalPosition + outpost.Comp.LocalAABB.Center;
                transform.SetLocalPosition(outpost.Owner, outpostXform.LocalPosition - correction, outpostXform);
                transform.SetLocalRotation(outpost.Owner, Angle.Zero, outpostXform);
                Snapshot(report, "5 CentreOutpost", mapId);

                // ---- Stage 6: first ReserveOutpostArea.
                ReserveOutpostArea(report, mapUid, outpost, outpostXform, ruleComp.ClearRadius);
                Snapshot(report, "6 ReserveOutpostArea#1", mapId);

                // ---- Stage 7: SpawnPois (new signature takes origin + bounds
                // rather than an EntityUid, since the outpost may not exist yet
                // when the spawner runs in production).
                var poiComp = new LavalandPoiSpawnerComponent();
                var origin = outpostXform.WorldPosition;
                var outpostBounds = outpost.Comp.LocalAABB.Translated(origin);
                poi.SpawnPois((EntityUid.Invalid, poiComp), mapUid, origin, outpostBounds);
                Snapshot(report, "7 SpawnPois", mapId);

                // ---- Stage 8: second ReserveOutpostArea.
                var finalCorrection = outpostXform.LocalPosition + outpost.Comp.LocalAABB.Center;
                transform.SetLocalPosition(outpost.Owner, outpostXform.LocalPosition - finalCorrection, outpostXform);
                ReserveOutpostArea(report, mapUid, outpost, outpostXform, ruleComp.ClearRadius);
                Snapshot(report, "8 ReserveOutpostArea#2", mapId);

                // ---- Stage 9: biome preload / chunk generation.
                var biomeComp = SEntMan.GetComponent<BiomeComponent>(mapUid);
                biome.Preload(mapUid, biomeComp, new Box2(-160, -160, 160, 160));
                Snapshot(report, "9 Preload", mapId);
            });

            // Chunks are only generated while the biome system ticks.
            await Pair.RunTicksSync(30);
            await Server.WaitPost(() => Snapshot(report, "10 after 30 ticks", MapIdOfOutpost()));

            await Pair.RunTicksSync(60);
            await Server.WaitPost(() => Snapshot(report, "11 after 90 ticks", MapIdOfOutpost()));
        }

        try
        {
            System.IO.File.AppendAllText("/tmp/lavaland-chain.log", report.ToString());
        }
        catch { }
    }

    private MapId _lastMapId;

    private MapId MapIdOfOutpost() => _lastMapId;

    private void ApplyPlanetOverrides(System.Text.StringBuilder report, EntityUid mapUid, LavalandOutpostRuleComponent component)
    {
        // Mirrors LavalandOutpostRuleSystem.ApplyPlanetOverrides for the parts that
        // touch the map entity. The atmosphere part is applied by the rule through
        // AtmosphereSystem, which is not relevant for entity bookkeeping.
        if (component.DisableLightCycle && SEntMan.TryGetComponent<LightCycleComponent>(mapUid, out var lightCycle))
        {
            lightCycle.Enabled = false;
        }

        if (component.RemoveRoof)
            SEntMan.RemoveComponent<RoofComponent>(mapUid);

        if (SEntMan.TryGetComponent<SunShadowComponent>(mapUid, out var sunShadow))
        {
            sunShadow.Direction = component.SunShadowDirection;
            sunShadow.Alpha = component.SunShadowAlpha;
        }
    }

    private void ReserveOutpostArea(
        System.Text.StringBuilder report,
        EntityUid mapUid,
        Entity<MapGridComponent> outpost,
        TransformComponent outpostXform,
        float clearRadius)
    {
        var localBounds = outpost.Comp.LocalAABB.Enlarged(clearRadius);
        var matrix = Matrix3x2.CreateRotation((float) outpostXform.LocalRotation) *
                     Matrix3x2.CreateTranslation(outpostXform.LocalPosition);
        var mapBounds = matrix.TransformBox(localBounds);

        var biome = SEntMan.System<BiomeSystem>();
        biome.ReserveTiles(mapUid, mapBounds, new List<(Vector2i, Tile)>());

        report.AppendLine($"  ReserveTiles(mapUid={mapUid}, bounds={mapBounds})");
    }

    private void Snapshot(System.Text.StringBuilder report, string stage, MapId mapId)
    {
        var entities = 0;
        var q = SEntMan.EntityQueryEnumerator<TransformComponent>();
        while (q.MoveNext(out _, out var xform))
        {
            if (xform.MapID == mapId)
                entities++;
        }

        report.AppendLine($"--- STAGE {stage} (mapId={mapId}) entitiesOnMap={entities}");
    }

    private void SnapshotOutpost(System.Text.StringBuilder report, string stage, EntityUid outpostUid)
    {
        report.AppendLine($"  --- OUTPOST {stage} grid={outpostUid}");

        var q = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (q.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.GridUid != outpostUid)
                continue;

            var id = meta.EntityPrototype?.ID;
            if (id is null)
                continue;

            if (id is not ("LockerMinerFilled" or "ClosetEmergencyFilledRandom" or "OxygenCanister"
                or "Pickaxe" or "SpaceHeater" or "RadioHandheld" or "EmergencyOxygenTankFilled"
                or "ToolboxEmergencyFilled" or "FlashlightLantern" or "WelderMini"))
            {
                continue;
            }

            report.AppendLine($"      {id} uid={uid} parent={(xform.ParentUid is { } p ? p.ToString() : "null")}" +
                              $" grid={(xform.GridUid is { } g ? g.ToString() : "null")}" +
                              $" mapId={xform.MapID}" +
                              $" local=({xform.LocalPosition.X:F3},{xform.LocalPosition.Y:F3})" +
                              $" world=({xform.WorldPosition.X:F3},{xform.WorldPosition.Y:F3})" +
                              $" anchored={xform.Anchored}" +
                              $" lifeStage={meta.EntityLifeStage}");
        }
    }
}
