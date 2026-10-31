#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Anomaly.Effects.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Server.Destructible;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Anomaly;

/// <summary>
///     Anomaly floors are destructible overlays: they are spawned by anomalies instead of replacing
///     station tiles, and melee damage removes them to reveal the original floor.
/// </summary>
[TestFixture]
public sealed class AnomalyFloorTest : GameTest
{
    private static IPrototypeManager Proto => IoCManager.Instance!.Resolve<IPrototypeManager>();

    private static readonly EntProtoId RockFloor = "AnomalyFloorRock";
    private static readonly EntProtoId FleshFloor = "AnomalyFloorFlesh";
    private static readonly EntProtoId GrassFloor = "AnomalyFloorGrass";

    private static readonly ProtoId<DamageTypePrototype> Slash = "Slash";

    [Test]
    public async Task AnomalyFloorPrototypesAreDestructible()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var id in new[] { RockFloor, FleshFloor, GrassFloor })
            {
                var proto = Proto.Index(id);
                Assert.That(proto.Components.ContainsKey("Damageable"), Is.True,
                    $"{id} must be damageable so it can be broken");
                Assert.That(proto.Components.ContainsKey("Destructible"), Is.True,
                    $"{id} must be destructible");
            }
        });
    }

    [Test]
    public async Task AnomalyFloorBreaksWithMeleeDamage()
    {
        var map = await Pair.CreateTestMap();
        EntityUid floor = default;

        await Server.WaitPost(() =>
        {
            floor = SEntMan.SpawnEntity(FleshFloor, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(floor), Is.True);
            Assert.That(SEntMan.GetComponent<TransformComponent>(floor).Anchored, Is.True);
        });

        await Server.WaitPost(() =>
        {
            var damageable = SEntMan.System<DamageableSystem>();
            damageable.TryChangeDamage(floor,
                new DamageSpecifier(Proto.Index(Slash), FixedPoint2.New(50)),
                ignoreResistances: true);
        });

        await Server.WaitAssertion(() =>
        {
            var damageable = SEntMan.GetComponent<DamageableComponent>(floor);
            var destructible = SEntMan.GetComponent<DestructibleComponent>(floor);
            var totalDamage = SEntMan.System<DamageableSystem>().GetTotalDamage((floor, damageable));
            Assert.That(totalDamage, Is.GreaterThan(FixedPoint2.Zero),
                "Damage should have been applied to the floor");
            Assert.That(destructible.Thresholds.Count, Is.GreaterThan(0),
                "The floor should have destruction thresholds");
        });

        await Server.WaitRunTicks(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(floor) || !SEntMan.EntityExists(floor), Is.True,
                "The anomaly floor should be destroyed by melee damage");
        });
    }

    [Test]
    public async Task AnomaliesSpawnFloorsInsteadOfTiles()
    {
        await Server.WaitAssertion(() =>
        {
            var flesh = Proto.Index<EntityPrototype>(new EntProtoId("AnomalyFlesh"));
            var flora = Proto.Index<EntityPrototype>(new EntProtoId("AnomalyFlora"));
            Assert.That(flesh.Components.ContainsKey("TileSpawnAnomaly"), Is.False,
                "Flesh anomaly should not replace station tiles");
            Assert.That(flora.Components.ContainsKey("TileSpawnAnomaly"), Is.False,
                "Flora anomaly should not replace station tiles");

            AssertFloorSpawns(flesh, FleshFloor.Id);
            AssertFloorSpawns(flora, GrassFloor.Id);

            foreach (var rockId in new[]
                     {
                         "AnomalyRockUranium", "AnomalyRockQuartz", "AnomalyRockSilver",
                         "AnomalyRockGold", "AnomalyRockIron", "AnomalyRockCoal",
                     })
            {
                var rock = Proto.Index<EntityPrototype>(new EntProtoId(rockId));
                Assert.That(rock.Components.ContainsKey("TileSpawnAnomaly"), Is.False,
                    $"{rockId} should not replace station tiles");
                AssertFloorSpawns(rock, RockFloor.Id);
            }
        });
    }

    private static void AssertFloorSpawns(EntityPrototype anomaly, string floorId)
    {
        Assert.That(anomaly.Components.ContainsKey("EntitySpawnAnomaly"), Is.True,
            $"{anomaly.ID} should spawn entities");
        var component = (EntitySpawnAnomalyComponent)anomaly.Components["EntitySpawnAnomaly"].Component;
        var spawns = component.Entries.SelectMany(entry => entry.Spawns).Select(spawn => spawn.Id).ToArray();
        Assert.That(spawns, Does.Contain(floorId),
            $"{anomaly.ID} should spawn {floorId}");
    }
}
