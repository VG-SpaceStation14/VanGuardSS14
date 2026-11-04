#nullable enable
using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Station.Components;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._VanGuard.Lavaland;

/// <summary>
/// Verifies the Blood Miner's two bodies, its damage-gated phase transition and
/// its phase-two drops.
/// </summary>
[TestFixture]
public sealed class BloodMinerTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        DummyTicker = false,
    };

    [Test]
    public async Task BloodMinerTransitionsAndDropsLoot()
    {
        var grid = GetStationGrid();
        EntityUid boss = default;
        var damage = new DamageSpecifier(
            SProtoMan.Index<DamageTypePrototype>(new ProtoId<DamageTypePrototype>("Blunt")),
            400);

        try
        {
            await Server.WaitPost(() =>
            {
                boss = SEntMan.SpawnEntity("LavalandMobBloodMiner", new EntityCoordinates(grid, 0, 0));
            });
            await Pair.RunTicksSync(3);

            await Server.WaitPost(() =>
            {
                Assert.That(SEntMan.EntityExists(boss), Is.True);
                Assert.That(Server.System<DamageableSystem>().TryChangeDamage(boss, damage, ignoreResistances: true), Is.True);
            });
            await Pair.RunTicksSync(5);

            await Server.WaitPost(() =>
            {
                Assert.That(SEntMan.EntityExists(boss), Is.False, "The phase-one boss was not replaced.");
                Assert.That(CountPrototype("LavalandMobBloodMiner"), Is.Zero);
                Assert.That(CountPrototype("LavalandMobBloodMinerBuffed"), Is.EqualTo(1),
                    "The phase-two boss did not spawn.");
            });

            EntityUid buffed = default;
            await Server.WaitPost(() =>
            {
                var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
                while (query.MoveNext(out var uid, out var meta))
                {
                    if (meta.EntityPrototype?.ID == "LavalandMobBloodMinerBuffed")
                        buffed = uid;
                }

                Assert.That(Server.System<DamageableSystem>().TryChangeDamage(buffed, damage, ignoreResistances: true), Is.True);
            });
            await Pair.RunTicksSync(5);

            await Server.WaitPost(() =>
            {
                Assert.That(SEntMan.EntityExists(buffed), Is.False, "The phase-two boss did not die.");
                Assert.That(CountPrototype("LavalandMobBloodMinerBuffed"), Is.Zero);
                Assert.That(CountPrototype("LavalandCleavingSaw"), Is.EqualTo(1),
                    "The phase-two boss did not drop the cleaving saw.");
                Assert.That(CountPrototype("WeaponProtoKineticAccelerator"), Is.EqualTo(1),
                    "The phase-two boss did not drop the proto-kinetic accelerator.");
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
                while (query.MoveNext(out var uid, out var meta))
                {
                    if (meta.EntityPrototype?.ID is "LavalandMobBloodMiner" or "LavalandMobBloodMinerBuffed"
                        or "LavalandCleavingSaw" or "WeaponProtoKineticAccelerator")
                    {
                        if (SEntMan.EntityExists(uid))
                            SEntMan.DeleteEntity(uid);
                    }
                }
            });
        }
    }

    [Test]
    public async Task BothArenaVariantsLoadAndSpawnPhaseOne()
    {
        var mapSystem = SEntMan.System<SharedMapSystem>();
        var loader = SEntMan.System<MapLoaderSystem>();
        var variants = new[]
        {
            "/Maps/_VanGuard/Lavaland/Dungeons/bloodminerarena.yml",
            "/Maps/_VanGuard/Lavaland/Dungeons/bloodminerarena2.yml",
        };

        foreach (var variant in variants)
        {
            MapId mapId = default;
            EntityUid mapUid = default;
            await Server.WaitPost(() =>
            {
                mapUid = mapSystem.CreateMap(out mapId);
                Assert.That(loader.TryLoadGrid(mapId, new ResPath(variant),
                    out _, new DeserializationOptions { InitializeMaps = true }), Is.True,
                    $"Could not load arena variant {variant}.");
            });
            await Pair.RunTicksSync(4);

            await Server.WaitPost(() =>
            {
                var bossCount = 0;
                var spawnerCount = 0;
                var query = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out _, out var meta, out var xform))
                {
                    if (xform.MapID != mapId)
                        continue;

                    if (meta.EntityPrototype?.ID == "LavalandMobBloodMiner")
                        bossCount++;
                    if (meta.EntityPrototype?.ID == "LavalandSpawnMobBloodMiner")
                        spawnerCount++;
                }

                Assert.That(bossCount, Is.EqualTo(1), $"Arena {variant} did not spawn one phase-one boss.");
                Assert.That(spawnerCount, Is.Zero, $"Arena {variant} did not consume its boss spawner.");
            });

            await Server.WaitPost(() =>
            {
                if (SEntMan.EntityExists(mapUid))
                    SEntMan.DeleteEntity(mapUid);
            });
        }
    }

    private int CountPrototype(string id)
    {
        var count = 0;
        var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == id)
                count++;
        }

        return count;
    }

    private EntityUid GetStationGrid()
    {
        var query = SEntMan.EntityQueryEnumerator<StationDataComponent>();
        var stationSystem = Server.System<Content.Server.Station.Systems.ServerStationSystem>();
        while (query.MoveNext(out var station, out var data))
        {
            if (stationSystem.GetLargestGrid((station, data)) is { } grid)
                return grid;
        }

        throw new InvalidOperationException("Test station has no grid.");
    }
}
