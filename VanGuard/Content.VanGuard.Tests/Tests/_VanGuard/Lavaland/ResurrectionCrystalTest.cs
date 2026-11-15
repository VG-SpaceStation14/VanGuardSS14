using System.Numerics;
#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._VanGuard.Lavaland;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Station.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

using Content.IntegrationTests.Fixtures;

namespace Content.IntegrationTests.Tests._VanGuard.Lavaland;

[TestFixture]
public sealed class ResurrectionCrystalTest : GameTest
{
    private static readonly EntProtoId ActiveCrystalProto = "LavalandResurrectionCrystalActive";

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        Fresh = true,
        DummyTicker = false,
    };

    [Test]
    public void ActiveCrystalIsHiddenFromSpawnMenu()
    {
        var proto = SProtoMan.Index<EntityPrototype>(ActiveCrystalProto);
        Assert.That(proto.HideSpawnMenu, Is.True,
            "The active resurrection crystal must be hidden from the spawn menu.");
    }

    [Test]
    public async Task CrystalRejectsLiveTarget()
    {
        var grid = GetStationGrid();
        EntityUid target = default;
        EntityUid user = default;
        EntityUid crystal = default;

        try
        {
            await Server.WaitPost(() =>
            {
                target = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 5000, 5000));
                user = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 5000, 5001));
                crystal = SEntMan.SpawnEntity("LavalandResurrectionCrystal",
                    new EntityCoordinates(grid, 5001, 5001));

                var clickLocation = SEntMan.GetComponent<TransformComponent>(target).Coordinates;
                var ev = new AfterInteractEvent(user, crystal, target, clickLocation, true);
                SEntMan.EventBus.RaiseLocalEvent(crystal, ev);
            });
            await Pair.RunTicksSync(4);

            await Server.WaitPost(() =>
            {
                Assert.That(SEntMan.EntityExists(crystal), Is.True,
                    "The crystal was consumed on a live target.");
                Assert.That(CountActiveCrystals(), Is.Zero,
                    "An active crystal was spawned on a live target.");
                Assert.That(SEntMan.GetComponent<MobStateComponent>(target).CurrentState,
                    Is.EqualTo(MobState.Alive));
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                DeleteIfExists(user);
                DeleteIfExists(target);
                DeleteIfExists(crystal);
                RemoveAllActiveCrystals();
            });
        }
    }

    [Test]
    public async Task CrystalSealsDeadBodyAndConsumesItem()
    {
        var grid = GetStationGrid();
        EntityUid body = default;
        EntityUid user = default;
        EntityUid crystal = default;

        try
        {
            await Server.WaitPost(() =>
            {
                body = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 5100, 5100));
                Kill(body);
            });
            await Pair.RunTicksSync(4);

            await Server.WaitPost(() =>
            {
                Assert.That(SEntMan.EntityExists(body), Is.True,
                    "The body no longer exists after Kill().");
                Assert.That(SEntMan.GetComponent<MobStateComponent>(body).CurrentState,
                    Is.EqualTo(MobState.Dead), "Test setup failed to kill the mob.");

                user = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 5100, 5101));
                crystal = SEntMan.SpawnEntity("LavalandResurrectionCrystal",
                    new EntityCoordinates(grid, 5101, 5101));

                var clickLocation = SEntMan.GetComponent<TransformComponent>(body).Coordinates;
                var ev = new AfterInteractEvent(user, crystal, body, clickLocation, true);
                SEntMan.EventBus.RaiseLocalEvent(crystal, ev);
            });
            await Pair.RunTicksSync(4);

            await Server.WaitPost(() =>
            {
                Assert.That(SEntMan.EntityExists(crystal), Is.False,
                    "The crystal item was not consumed.");

                var activeCrystal = FindActiveCrystal();
                Assert.That(activeCrystal, Is.Not.Null, "No active crystal was spawned.");

                var containers = SEntMan.System<SharedContainerSystem>();
                Assert.That(
                    containers.TryGetContainer(activeCrystal!.Value,
                        ActiveResurrectionCrystalComponent.BodyContainer, out var cont),
                    Is.True, "Active crystal has no body_slot container.");
                Assert.That(cont!.ContainedEntities.Any(e => e == body), Is.True,
                    "The dead body was not sealed inside the active crystal.");
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                DeleteIfExists(user);
                DeleteIfExists(body);
                DeleteIfExists(crystal);
                RemoveAllActiveCrystals();
            });
        }
    }

    [Test]
    public async Task ActiveCrystalRevivesBodyOnTimer()
    {
        var grid = GetStationGrid();
        EntityUid body = default;
        EntityUid crystal = default;

        try
        {
            await Server.WaitPost(() =>
            {
                body = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 5200, 5200));
                Kill(body);
            });
            await Pair.RunTicksSync(4);

            await Server.WaitPost(() =>
            {
                Assert.That(SEntMan.EntityExists(body), Is.True,
                    "The body no longer exists after Kill().");
                Assert.That(SEntMan.GetComponent<MobStateComponent>(body).CurrentState,
                    Is.EqualTo(MobState.Dead));

                crystal = SEntMan.SpawnEntity(ActiveCrystalProto,
                    SEntMan.GetComponent<TransformComponent>(body).Coordinates);

                var activeComp = SEntMan.GetComponent<ActiveResurrectionCrystalComponent>(crystal);
                activeComp.ReviveTime = TimeSpan.FromSeconds(1);

                var containers = SEntMan.System<SharedContainerSystem>();
                var container = containers.EnsureContainer<Container>(
                    crystal, ActiveResurrectionCrystalComponent.BodyContainer);
                Assert.That(containers.Insert(body, container, force: true), Is.True,
                    "Could not insert the body into the active crystal container.");

                activeComp.HeldEntity = body;
                activeComp.ReviveAt = Server.ResolveDependency<IGameTiming>().CurTime
                                      + activeComp.ReviveTime;
            });
            await Pair.RunTicksSync(120);

            await Server.WaitPost(() =>
            {
                Assert.That(SEntMan.EntityExists(crystal), Is.False,
                    "The active crystal was not consumed after revival.");
                Assert.That(SEntMan.EntityExists(body), Is.True,
                    "The revived body no longer exists.");
                Assert.That(SEntMan.GetComponent<MobStateComponent>(body).CurrentState,
                    Is.EqualTo(MobState.Alive), "The body was not revived.");
                Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(body).Float(),
                    Is.EqualTo(0f), "The revived body still has damage.");
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                DeleteIfExists(body);
                DeleteIfExists(crystal);
                RemoveAllActiveCrystals();
            });
        }
    }

    [Test]
    public async Task ActiveCrystalBreaksAndDropsBodyStillDead()
    {
        var grid = GetStationGrid();
        EntityUid body = default;
        EntityUid crystal = default;

        try
        {
            await Server.WaitPost(() =>
            {
                body = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 5300, 5300));
                Kill(body);
            });
            await Pair.RunTicksSync(4);

            await Server.WaitPost(() =>
            {
                Assert.That(SEntMan.EntityExists(body), Is.True,
                    "The body no longer exists after Kill().");
                Assert.That(SEntMan.GetComponent<MobStateComponent>(body).CurrentState,
                    Is.EqualTo(MobState.Dead));

                crystal = SEntMan.SpawnEntity(ActiveCrystalProto,
                    SEntMan.GetComponent<TransformComponent>(body).Coordinates);

                var activeComp = SEntMan.GetComponent<ActiveResurrectionCrystalComponent>(crystal);
                activeComp.ReviveTime = TimeSpan.FromSeconds(60);

                var containers = SEntMan.System<SharedContainerSystem>();
                var container = containers.EnsureContainer<Container>(
                    crystal, ActiveResurrectionCrystalComponent.BodyContainer);
                Assert.That(containers.Insert(body, container, force: true), Is.True);

                activeComp.HeldEntity = body;
                activeComp.ReviveAt = Server.ResolveDependency<IGameTiming>().CurTime
                                      + activeComp.ReviveTime;

                // 50 is the destruction threshold in the prototype, so 100 is well past it.
                var crystalDamage = new DamageSpecifier(
                    SProtoMan.Index<DamageTypePrototype>(new ProtoId<DamageTypePrototype>("Blunt")),
                    100);
                SEntMan.System<DamageableSystem>()
                    .TryChangeDamage(crystal, crystalDamage, ignoreResistances: true);
            });
            await Pair.RunTicksSync(6);

            await Server.WaitPost(() =>
            {
                Assert.That(SEntMan.EntityExists(crystal), Is.False,
                    "The broken crystal still exists.");
                Assert.That(SEntMan.EntityExists(body), Is.True,
                    "The body was deleted together with the broken crystal.");
                Assert.That(SEntMan.GetComponent<MobStateComponent>(body).CurrentState,
                    Is.EqualTo(MobState.Dead),
                    "The body was revived despite the crystal being broken.");
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                DeleteIfExists(body);
                DeleteIfExists(crystal);
                RemoveAllActiveCrystals();
            });
        }
    }

    private void RemoveAllActiveCrystals()
    {
        var query = SEntMan.EntityQueryEnumerator<ActiveResurrectionCrystalComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (SEntMan.EntityExists(uid))
                SEntMan.DeleteEntity(uid);
        }
    }

    /// <summary>
    /// Kills the mob by directly transitioning it to <see cref="MobState.Dead"/>.
    /// Applying lethal damage is not safe: 400 blunt damage with ignored
    /// resistances also crosses the gib threshold and deletes the entity.
    /// </summary>
    private void Kill(EntityUid uid)
    {
        var mobState = SEntMan.GetComponent<MobStateComponent>(uid);
        SEntMan.System<MobStateSystem>().ChangeMobState(uid, MobState.Dead, mobState);
    }

    private int CountActiveCrystals()
    {
        var count = 0;
        var query = SEntMan.EntityQueryEnumerator<ActiveResurrectionCrystalComponent>();
        while (query.MoveNext(out _, out _))
            count++;
        return count;
    }

    private EntityUid? FindActiveCrystal()
    {
        var query = SEntMan.EntityQueryEnumerator<ActiveResurrectionCrystalComponent>();
        while (query.MoveNext(out var uid, out _))
            return uid;
        return null;
    }

    private void DeleteIfExists(EntityUid uid)
    {
        if (uid.IsValid() && SEntMan.EntityExists(uid))
            SEntMan.DeleteEntity(uid);
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
