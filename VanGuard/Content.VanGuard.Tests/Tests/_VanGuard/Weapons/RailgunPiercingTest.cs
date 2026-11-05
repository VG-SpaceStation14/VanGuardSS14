using System.Numerics;
using Content.IntegrationTests;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._VanGuard.Weapons;

/// <summary>
///     Regression tests for the kinetic railgun: the bolt should fly through rock walls and destroy them,
///     while still stopping at the first creature it hits.
/// </summary>
[TestFixture]
public sealed class RailgunPiercingTest : GameTest
{
    // These tests spawn living mobs on the shared test map. A fast-recycled pair can still carry
    // deferred work from a previous test, which then surfaces during our teardown. Use a fresh pair
    // so no state from another fixture can leak into the railgun spawn/cleanup cycle.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true, Fresh = true };

    [Test]
    [TestCase("AsteroidRock")]
    [TestCase("IronRock")]
    [TestCase("WallRock")]
    public async Task RailgunDestroysSeveralRockWalls(string rockProto)
    {
        var map = await Pair.CreateTestMap();
        var coords = new EntityCoordinates(map.Grid, Vector2.Zero);

        EntityUid first = default;
        EntityUid second = default;

        await Server.WaitPost(() =>
        {
            first = SSpawnAtPosition(rockProto, coords);
            second = SSpawnAtPosition(rockProto, coords.Offset(new Vector2(1f, 0f)));

            SpawnBolt(coords.Offset(new Vector2(-2f, 0f)));
        });

        await RunSeconds(0.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(first), Is.False, $"First {rockProto} should be destroyed by the railgun");
            Assert.That(SEntMan.EntityExists(second), Is.False,
                $"Second {rockProto} should also be destroyed: the railgun bolt should pierce through rock walls");
        });
    }

    [Test]
    [Description("The railgun bolt damages the first creature it hits, is spent afterwards and does not pierce it.")]
    public async Task RailgunStopsAtFirstCreature()
    {
        var map = await Pair.CreateTestMap();
        var coords = new EntityCoordinates(map.Grid, Vector2.Zero);

        EntityUid first = default;
        EntityUid second = default;
        EntityUid control = default;
        EntityUid bolt = default;

        await Server.WaitPost(() =>
        {
            // Two creatures on the bolt's path, plus an off-path control to account for environmental damage.
            first = SSpawnAtPosition("MobHuman", coords);
            second = SSpawnAtPosition("MobHuman", coords.Offset(new Vector2(1f, 0f)));
            control = SSpawnAtPosition("MobHuman", coords.Offset(new Vector2(0f, 5f)));

            bolt = SpawnBolt(coords.Offset(new Vector2(-2f, 0f)));
        });

        // Enough for the bolt to hit the first creature, but not enough for it to time out.
        await RunSeconds(0.12f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(bolt), Is.True,
                "The bolt should still be flying right after the first hit");

            Assert.That(SEntMan.GetComponent<ProjectileComponent>(bolt).ProjectileSpent, Is.True,
                "The bolt should be spent after hitting a creature");

            Assert.That(GetKineticDamage(first), Is.GreaterThan(FixedPoint2.Zero),
                "The first creature should be damaged by the railgun");

            Assert.That(GetKineticDamage(second), Is.EqualTo(FixedPoint2.Zero),
                "The second creature should not be damaged: the bolt does not penetrate creatures");
        });

        // Let the bolt fly out the rest of its lifetime: the spent bolt must not damage the second creature.
        await RunSeconds(0.4f);

        await Server.WaitAssertion(() =>
        {
            var controlDamage = GetKineticDamage(control);

            Assert.That(GetKineticDamage(first), Is.GreaterThan(controlDamage),
                "The first creature should have taken the railgun hit");

            Assert.That(GetKineticDamage(second), Is.EqualTo(controlDamage),
                "The second creature should only have the environmental damage of an untouched creature");
        });
    }

    /// <summary>
    ///     Returns the amount of kinetic (blunt) damage a target has taken, which is the damage the railgun deals
    ///     to living creatures. Used to tell the railgun hit apart from environmental damage.
    /// </summary>
    private FixedPoint2 GetKineticDamage(EntityUid target)
    {
        var damageable = SEntMan.System<DamageableSystem>();
        var damage = damageable.GetAllDamage((target, null));

        foreach (var (type, amount) in damage.DamageDict)
        {
            if (type.Id == "Blunt")
                return amount;
        }

        return FixedPoint2.Zero;
    }

    private EntityUid SpawnBolt(EntityCoordinates coords)
    {
        var bolt = SSpawnAtPosition("BulletKineticRailgun", coords);
        var body = SEntMan.GetComponent<PhysicsComponent>(bolt);
        SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(bolt, new Vector2(30f, 0f), body: body);
        return bolt;
    }
}
