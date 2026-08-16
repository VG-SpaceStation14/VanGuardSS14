#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Trigger.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Mining;

/// <summary>
/// Verifies that the volatile gibtonite variant placed on the planetoid (VGRoid) explodes
/// IMMEDIATELY on any damage (no timer), while the standard gibtonite only arms its timer
/// once enough damage is dealt.
/// </summary>
[TestFixture]
public sealed class GibtoniteTest : GameTest
{
    private static readonly ProtoId<DamageTypePrototype> HeatDamage = new("Heat");

    private static async Task<EntityUid> SpawnRock(
        Robust.UnitTesting.IServerIntegrationInstance server,
        IEntityManager entMan,
        string prototype,
        EntityCoordinates coords)
    {
        EntityUid uid = default;
        await server.WaitPost(() => uid = entMan.SpawnEntity(new ProtoId<EntityPrototype>(prototype), coords));
        await server.WaitRunTicks(3);
        return uid;
    }

    [Test]
    public async Task VolatileGibtonite_ExplodesOnAnyDamage()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var systems = server.ResolveDependency<IEntitySystemManager>();
        var damageableSystem = systems.GetEntitySystem<DamageableSystem>();

        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();

        // Anchored walls (rocks) must be spawned on a grid.
        var gib = await SpawnRock(server, entMan, "IronRockGibtoniteVolatile", testMap.GridCoords);

        // A light impact (2 heat damage) must set it off instantly - touch it, boom.
        var heat = new DamageSpecifier(protoMan.Index(HeatDamage), FixedPoint2.New(2));
        await server.WaitPost(() => damageableSystem.TryChangeDamage(gib, heat, true));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(gib), Is.True,
                "Volatile gibtonite should explode and be destroyed instantly from a light impact.");
        });
    }

    [Test]
    public async Task StandardGibtonite_DoesNotExplodeOnLightDamage()
    {
        var server = Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var systems = server.ResolveDependency<IEntitySystemManager>();
        var damageableSystem = systems.GetEntitySystem<DamageableSystem>();

        var testMap = await Pair.CreateTestMap();
        await server.WaitIdleAsync();

        var gib = await SpawnRock(server, entMan, "IronRockGibtonite", testMap.GridCoords);

        var heat = new DamageSpecifier(protoMan.Index(HeatDamage), FixedPoint2.New(2));
        await server.WaitPost(() => damageableSystem.TryChangeDamage(gib, heat, true));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(gib), Is.False,
                "Standard gibtonite should survive a light impact.");
            Assert.That(entMan.HasComponent<ActiveTimerTriggerComponent>(gib), Is.False,
                "Standard gibtonite should NOT arm its timer from a light impact.");
        });

        // A heavier hit (total 6 damage) arms the standard gibtonite's timer.
        await server.WaitPost(() => damageableSystem.TryChangeDamage(gib, heat * 2, true));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<ActiveTimerTriggerComponent>(gib), Is.True,
                "Standard gibtonite should arm its timer once enough damage is dealt.");
        });
    }
}
