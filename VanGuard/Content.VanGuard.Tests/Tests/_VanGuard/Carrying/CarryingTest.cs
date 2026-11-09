using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._VanGuard.Carrying;
using Content.Shared._VanGuard.Carrying;
using Content.Server.Buckle.Systems;
using Content.Server.Cuffs;
using Content.Server.Mind;
using Content.Server.Stunnable;
using Content.Shared.ActionBlocker;
using Content.Shared.Alert;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.Chasm;
using Content.Shared.Climbing.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Movement.Systems;
using Content.Shared.Rotation;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Standing;
using Content.Shared.Verbs;
using Content.IntegrationTests;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Shared.GameObjects;
using Robust.Client.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._VanGuard.Carrying;

[TestFixture]
[NonParallelizable]
public sealed class CarryingTest : GameTest
{
    private static PoolSettings SleepOnlySettings => new() { Connected = false };

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    private async Task<(EntityUid Carrier, EntityUid Target, MapId Map)> CreatePair(string proto = "MobHuman")
    {
        EntityUid? carrier = null;
        EntityUid? target = null;
        var mapId = MapId.Nullspace;

        await Server.WaitPost(() =>
        {
            var map = Server.System<SharedMapSystem>();
            map.CreateMap(out mapId);
            carrier = SEntMan.SpawnEntity(proto, new MapCoordinates(Vector2.Zero, mapId));
            target = SEntMan.SpawnEntity(proto, new MapCoordinates(new Vector2(1, 0), mapId));
        });

        Assert.That(carrier, Is.Not.Null);
        Assert.That(target, Is.Not.Null);
        return (carrier!.Value, target!.Value, mapId);
    }

    private async Task StartCarry(EntityUid carrier, EntityUid target)
    {
        var system = Server.System<CarryingSystem>();
        await Server.WaitPost(() => Assert.That(system.TryStartCarry(carrier, target), Is.True));
        await Pair.RunTicksSync(140);
    }

    [Test]
    public async Task CarryAttachesFollowsAndReleases()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();

        await StartCarry(carrier, target);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CarryingComponent>(carrier).Carried, Is.EqualTo(target));
            Assert.That(SEntMan.GetComponent<CarriedComponent>(target).Carrier, Is.EqualTo(carrier));
            Assert.That(SEntMan.HasComponent<HumanoidProfileComponent>(carrier), Is.True);
            Assert.That(SEntMan.HasComponent<HumanoidProfileComponent>(target), Is.True);

            var buckle = SEntMan.GetComponent<BuckleComponent>(target);
            Assert.That(buckle.Buckled, Is.True);
            var anchor = SEntMan.GetComponent<CarryingComponent>(carrier).Anchor;
            Assert.That(anchor, Is.Not.Null);
            Assert.That(buckle.BuckledTo, Is.EqualTo(anchor));
            Assert.That(SEntMan.GetComponent<TransformComponent>(target).ParentUid, Is.EqualTo(anchor));
            Assert.That(SEntMan.GetComponent<TransformComponent>(anchor!.Value).ParentUid, Is.EqualTo(carrier));
            Assert.That(SEntMan.GetComponent<HandsComponent>(carrier).Hands.Count, Is.EqualTo(2));
        });

        // The carried body must follow the carrier's transform, not stay at its
        // original map position.
        var targetBefore = default(Vector2);
        var carrierPosition = default(Vector2);
        await Server.WaitAssertion(() =>
        {
            targetBefore = SEntMan.GetComponent<TransformComponent>(target).WorldPosition;
            carrierPosition = SEntMan.GetComponent<TransformComponent>(carrier).WorldPosition;
        });

        await Server.WaitPost(() =>
            Server.System<Robust.Shared.GameObjects.SharedTransformSystem>()
                .SetWorldPosition(carrier, carrierPosition + new Vector2(3, 0)));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var targetAfter = SEntMan.GetComponent<TransformComponent>(target).WorldPosition;
            Assert.That((targetAfter - targetBefore).Length, Is.GreaterThan(2f));
        });

        await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
            Assert.That(SEntMan.GetComponent<BuckleComponent>(target).Buckled, Is.False);
            Assert.That(SEntMan.GetComponent<TransformComponent>(target).ParentUid, Is.Not.EqualTo(carrier));
        });
    }

    [Test]
    public async Task CarryModeAlertTogglesAndClearsOnCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();

        await Server.WaitPost(() => Assert.That(system.TryToggleCarryMode(carrier), Is.True));
        await Server.WaitAssertion(() =>
            Assert.That(Server.System<AlertsSystem>().IsShowingAlert(carrier, "CarryMode"), Is.True));

        await Server.WaitPost(() => Assert.That(system.TryToggleCarryMode(carrier), Is.True));
        await Server.WaitAssertion(() =>
            Assert.That(Server.System<AlertsSystem>().IsShowingAlert(carrier, "CarryMode"), Is.False));

        await Server.WaitPost(() => Assert.That(system.TryToggleCarryMode(carrier), Is.True));
        await Server.WaitAssertion(() =>
            Assert.That(Server.System<AlertsSystem>().IsShowingAlert(carrier, "CarryMode"), Is.True));

        await Server.WaitPost(() =>
            SEntMan.EventBus.RaiseLocalEvent(target, new InteractHandEvent(carrier, target)));
        await Pair.RunTicksSync(140);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<AlertsSystem>().IsShowingAlert(carrier, "CarryMode"), Is.False);
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.True);
        });
    }

    [Test]
    public async Task CarryAppliesSlowdownAndBlocksTargetMovement()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        var movement = SEntMan.System<MovementSpeedModifierSystem>();
        var blocker = SEntMan.System<ActionBlockerSystem>();
        var hands = SEntMan.System<SharedHandsSystem>();

        var baseWalk = 0f;
        await Server.WaitAssertion(() =>
        {
            baseWalk = SEntMan.GetComponent<MovementSpeedModifierComponent>(carrier).BaseWalkSpeed;
        });

        await StartCarry(carrier, target);

        await Server.WaitAssertion(() =>
        {
            var move = SEntMan.GetComponent<MovementSpeedModifierComponent>(carrier);
            Assert.That(move.CurrentWalkSpeed, Is.LessThan(baseWalk));
            Assert.That(blocker.CanMove(target), Is.False);
            Assert.That(hands.CountFreeHands(carrier), Is.EqualTo(0));
        });

        await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var move = SEntMan.GetComponent<MovementSpeedModifierComponent>(carrier);
            Assert.That(move.CurrentWalkSpeed, Is.EqualTo(baseWalk).Within(0.001f));
            Assert.That(blocker.CanMove(target), Is.True);
            Assert.That(hands.CountFreeHands(carrier), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task CannotStartTwoCarriesOrCarryTheSameTargetTwice()
    {
        var (carrier, target, _) = await CreatePair();
        EntityUid? third = null;
        await Server.WaitPost(() =>
            third = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(2, 0), SEntMan.GetComponent<TransformComponent>(carrier).MapID)));
        var thirdUid = third!.Value;
        var system = Server.System<CarryingSystem>();

        await StartCarry(carrier, target);

        await Server.WaitPost(() =>
        {
            Assert.That(system.TryStartCarry(carrier, thirdUid), Is.False,
                "A carrier cannot pick up a second person");
            Assert.That(system.TryStartCarry(thirdUid, target), Is.False,
                "A carried person cannot be picked up by a second carrier");
            Assert.That(system.TryStartCarry(target, thirdUid), Is.False,
                "A carried person cannot start carrying someone else");
        });

        await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
        await Pair.RunTicksSync(5);
    }

    [Test]
    public async Task SelfAndInvalidTargetsAreRejected()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();

        await Server.WaitPost(() =>
        {
            Assert.That(system.TryStartCarry(carrier, carrier), Is.False);

            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 300 } };
            SEntMan.System<DamageableSystem>().TryChangeDamage(target, damage, ignoreResistances: true);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitPost(() => Assert.That(system.TryStartCarry(carrier, target), Is.False));
    }

    [Test]
    public async Task CarrierDeathEndsCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() =>
        {
            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 300 } };
            SEntMan.System<DamageableSystem>().TryChangeDamage(carrier, damage, ignoreResistances: true);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
            Assert.That(SEntMan.GetComponent<BuckleComponent>(target).Buckled, Is.False);
        });
    }

    [Test]
    public async Task TargetDeathEndsCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() =>
        {
            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 300 } };
            SEntMan.System<DamageableSystem>().TryChangeDamage(target, damage, ignoreResistances: true);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });
    }

    [Test]
    public async Task CarrierDeletionEndsCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() => SEntMan.QueueDeleteEntity(carrier));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
            Assert.That(SEntMan.GetComponent<BuckleComponent>(target).Buckled, Is.False);
        });
    }

    [Test]
    public async Task TargetDeletionEndsCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() => SEntMan.QueueDeleteEntity(target));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False));
    }

    [Test]
    public async Task DisconnectEndsCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() =>
            SEntMan.EventBus.RaiseLocalEvent(carrier, new PlayerDetachedEvent(carrier, ServerSession!)));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });
    }

    [Test]
    public async Task MindTransferEndsCarry()
    {
        var (carrier, target, map) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await Server.WaitPost(() =>
            Assert.That(Server.PlayerMan.SetAttachedEntity(ServerSession!, carrier, force: true), Is.True));
        await StartCarry(carrier, target);

        EntityUid? newBody = null;
        await Server.WaitPost(() =>
        {
            newBody = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(4, 0), map));
            Server.System<MindSystem>().ControlMob(ServerSession!.UserId, newBody.Value);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });
    }

    [Test]
    public async Task StalePickupDoAfterDoesNotLeaveCarryState()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();

        await Server.WaitPost(() => Assert.That(system.TryStartCarry(carrier, target), Is.True));
        await Server.WaitPost(() => SEntMan.QueueDeleteEntity(target));
        await Pair.RunTicksSync(140);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
        });
    }

    [Test]
    public async Task RepeatedPickupRequestDoesNotDuplicateCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();

        await Server.WaitPost(() =>
        {
            Assert.That(system.TryStartCarry(carrier, target), Is.True);
            Assert.That(system.TryStartCarry(carrier, target), Is.False);
        });
        await Pair.RunTicksSync(140);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CarryingComponent>(carrier).Carried, Is.EqualTo(target));
            Assert.That(SEntMan.GetComponent<CarriedComponent>(target).Carrier, Is.EqualTo(carrier));
        });

        await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
        await Pair.RunTicksSync(5);
    }

    [Test]
    public async Task CarriedTargetCanBreakFree()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() =>
        {
            var mover = SEntMan.GetComponent<InputMoverComponent>(target);
            mover.HeldMoveButtons = MoveButtons.Left;
            var ev = new MoveInputEvent((target, mover), MoveButtons.None);
            SEntMan.EventBus.RaiseLocalEvent(target, ref ev);
        });
        await Pair.RunTicksSync(140);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });
    }


    [Test]
    public async Task CarrierCannotCarryWhilePulling()
    {
        var (carrier, target, _) = await CreatePair();
        EntityUid? third = null;
        await Server.WaitPost(() =>
            third = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(2, 0), SEntMan.GetComponent<TransformComponent>(carrier).MapID)));
        var pulling = Server.System<PullingSystem>();
        var system = Server.System<CarryingSystem>();

        await Server.WaitPost(() => Assert.That(pulling.TryStartPull(carrier, third!.Value), Is.True));
        await Server.WaitPost(() => Assert.That(system.TryStartCarry(carrier, target), Is.False));
        await Server.WaitPost(() =>
        {
            var pullable = SEntMan.GetComponent<PullableComponent>(third!.Value);
            pulling.TryStopPull(third!.Value, pullable, carrier);
        });
    }

    [Test]
    public async Task CannotCarryBuckledTarget()
    {
        var (carrier, target, _) = await CreatePair();
        EntityUid? chair = null;
        await Server.WaitPost(() =>
            chair = SEntMan.SpawnEntity("Chair", SEntMan.GetComponent<TransformComponent>(target).Coordinates));

        var buckle = Server.System<BuckleSystem>();
        var system = Server.System<CarryingSystem>();

        await Server.WaitPost(() => Assert.That(buckle.TryBuckle(target, target, chair!.Value), Is.True));
        await Pair.RunTicksSync(10);
        await Server.WaitPost(() => Assert.That(system.TryStartCarry(carrier, target), Is.False));
        await Pair.RunTicksSync(5);
        await Server.WaitPost(() => Assert.That(buckle.TryUnbuckle(target, target), Is.True));
        await Pair.RunTicksSync(5);
    }

    [Test]
    public async Task TargetDisconnectEndsCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() =>
            SEntMan.EventBus.RaiseLocalEvent(target, new PlayerDetachedEvent(target, ServerSession!)));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });
    }

    [Test]
    public async Task CarrierCarryingComponentLossUsesCentralCleanup()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        var hands = SEntMan.System<SharedHandsSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() => SEntMan.RemoveComponent<CarryingComponent>(carrier));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
            Assert.That(SEntMan.GetComponent<BuckleComponent>(target).Buckled, Is.False);
            Assert.That(hands.CountFreeHands(carrier), Is.EqualTo(2));
            var anchors = 0;
            var query = SEntMan.EntityQueryEnumerator<CarryAnchorComponent>();
            while (query.MoveNext(out _, out _))
                anchors++;
            Assert.That(anchors, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task SimultaneousCarriersCannotClaimSameTarget()
    {
        var (carrier, target, _) = await CreatePair();
        EntityUid? secondCarrier = null;
        await Server.WaitPost(() =>
            secondCarrier = SEntMan.SpawnEntity("MobHuman",
                new MapCoordinates(new Vector2(0, 1), SEntMan.GetComponent<TransformComponent>(carrier).MapID)));

        var other = secondCarrier!.Value;
        var system = Server.System<CarryingSystem>();
        var first = false;
        var second = false;
        await Server.WaitPost(() =>
        {
            first = system.TryStartCarry(carrier, target);
            second = system.TryStartCarry(other, target);
        });

        Assert.That(first ^ second, Is.True, "exactly one carrier may own the pickup attempt");
        await Pair.RunTicksSync(140);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier) ^
                        SEntMan.HasComponent<CarryingComponent>(other), Is.True);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.True);
            Assert.That(SEntMan.GetComponent<CarriedComponent>(target).Carrier,
                Is.EqualTo(first ? carrier : other));
        });

        await Server.WaitPost(() =>
        {
            if (SEntMan.HasComponent<CarryingComponent>(carrier))
                Assert.That(system.TryEndCarry(carrier, carrier), Is.True);
            if (SEntMan.HasComponent<CarryingComponent>(other))
                Assert.That(system.TryEndCarry(other, other), Is.True);
        });
        await Pair.RunTicksSync(5);
    }

    [Test]
    public async Task DisconnectDuringPickupCancelsAttempt()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await Server.WaitPost(() => Assert.That(system.TryStartCarry(carrier, target), Is.True));

        await Server.WaitPost(() =>
            SEntMan.EventBus.RaiseLocalEvent(carrier, new PlayerDetachedEvent(carrier, ServerSession!)));
        await Pair.RunTicksSync(140);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryAttemptComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarryAttemptTargetComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
        });
    }

    [Test]
    public async Task CarryHandlesCarrierRotations()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        var transform = Server.System<Robust.Shared.GameObjects.SharedTransformSystem>();

        foreach (var degrees in new[] { 90, 180, 270 })
        {
            await Server.WaitPost(() =>
                transform.SetWorldRotation(carrier, Angle.FromDegrees(degrees)));
            await StartCarry(carrier, target);

            await Server.WaitAssertion(() =>
            {
                var carrying = SEntMan.GetComponent<CarryingComponent>(carrier);
                Assert.That(carrying.Anchor, Is.Not.Null);
                var anchor = carrying.Anchor!.Value;
                var offset = SEntMan.GetComponent<StrapComponent>(anchor).BuckleOffset;
                var expected = transform.GetWorldPosition(carrier) +
                               transform.GetWorldRotation(carrier).RotateVec(offset);
                Assert.That((transform.GetWorldPosition(target) - expected).Length(),
                    Is.LessThan(0.01f));
                Assert.That(SEntMan.GetComponent<RotationVisualsComponent>(target).HorizontalRotation,
                    Is.EqualTo(Angle.FromDegrees(75)));
            });

            await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
            await Pair.RunTicksSync(10);
            await Server.WaitAssertion(() =>
                Assert.That(SEntMan.GetComponent<RotationVisualsComponent>(target).HorizontalRotation,
                    Is.EqualTo(Angle.FromDegrees(90))));
        }
    }

    [Test]
    public async Task CarrierComponentLossEndsCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() => SEntMan.RemoveComponent<HandsComponent>(carrier));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });
    }


    [Test]
    public async Task DetachingCarriedTransformEndsCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() =>
            Server.System<Robust.Shared.GameObjects.SharedTransformSystem>().AttachToGridOrMap(target));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });
    }

    [Test]
    public async Task TargetCarriedComponentLossRestoresRotation()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() => SEntMan.RemoveComponent<CarriedComponent>(target));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
            Assert.That(SEntMan.GetComponent<RotationVisualsComponent>(target).HorizontalRotation,
                Is.EqualTo(Angle.FromDegrees(90)));
        });
    }


    [Test]
    public async Task TargetComponentLossEndsCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() => SEntMan.RemoveComponent<BuckleComponent>(target));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });
    }


    [Test]
    public async Task CarryVerbStartsPickup()
    {
        var (carrier, target, _) = await CreatePair();
        InteractionVerb verb = null;

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.GetComponent<HandsComponent>(carrier);
            var verbs = new GetVerbsEvent<InteractionVerb>(
                carrier, target, null, hands, true, true, true, new List<VerbCategory>());
            SEntMan.EventBus.RaiseLocalEvent(target, verbs);
            verb = verbs.Verbs.FirstOrDefault(v => v.Text == Loc.GetString("carry-verb"));
            Assert.That(verb, Is.Not.Null, "The carry verb should be offered for a valid target");
        });

        await Server.WaitPost(() => verb!.Act?.Invoke());
        await Pair.RunTicksSync(140);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CarryingComponent>(carrier).Carried, Is.EqualTo(target));
            Assert.That(SEntMan.GetComponent<CarriedComponent>(target).Carrier, Is.EqualTo(carrier));
        });
    }




    [Test]
    public async Task CarryEntryAnimationMovesAnchorIntoHands()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();

        await Server.WaitPost(() => Assert.That(system.TryStartCarry(carrier, target), Is.True));
        await Pair.RunTicksSync(65);

        Vector2 startLocal = default;
        Vector2 currentLocal = default;
        Vector2 finalLocal = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.True);
            var carrying = SEntMan.GetComponent<CarryingComponent>(carrier);
            startLocal = carrying.AnchorStartLocal;
            currentLocal = SEntMan.GetComponent<TransformComponent>(carrying.Anchor!.Value).LocalPosition;
            Assert.That(startLocal.Length(), Is.GreaterThan(0.1f));
            Assert.That((currentLocal - startLocal).Length(), Is.GreaterThan(0.01f));
        });

        await Pair.RunTicksSync(15);
        await Server.WaitAssertion(() =>
        {
            var carrying = SEntMan.GetComponent<CarryingComponent>(carrier);
            finalLocal = SEntMan.GetComponent<TransformComponent>(carrying.Anchor!.Value).LocalPosition;
            Assert.That(finalLocal, Is.EqualTo(Vector2.Zero));
        });

        await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
        await Pair.RunTicksSync(5);
    }

    [Test]
    public async Task CarryRotationPoseIsAppliedAndRestored()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await Server.WaitPost(() =>
            Assert.That(Server.PlayerMan.SetAttachedEntity(ServerSession!, carrier, force: true), Is.True));
        await StartCarry(carrier, target);
        await Pair.RunTicksSync(20);

        await Client.WaitAssertion(() =>
        {
            var clientTarget = ToClientUid(target);
            Assert.That(CEntMan.HasComponent<CarriedComponent>(clientTarget), Is.True);
            var rotation = CEntMan.GetComponent<RotationVisualsComponent>(clientTarget);
            Assert.That(rotation.HorizontalRotation, Is.EqualTo(Angle.FromDegrees(75)));
            Assert.That(CEntMan.GetComponent<SpriteComponent>(clientTarget).Rotation,
                Is.EqualTo(Angle.FromDegrees(75)));
        });

        await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
        await Pair.RunTicksSync(30);

        await Client.WaitAssertion(() =>
        {
            var clientTarget = ToClientUid(target);
            var rotation = CEntMan.GetComponent<RotationVisualsComponent>(clientTarget);
            Assert.That(rotation.HorizontalRotation, Is.EqualTo(Angle.FromDegrees(90)));
            Assert.That(CEntMan.GetComponent<SpriteComponent>(clientTarget).Rotation,
                Is.EqualTo(Angle.Zero));
        });
    }


    [Test]
    public async Task CriticalTargetCanBeCarriedAndStaysDownOnRelease()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();

        await Server.WaitPost(() =>
        {
            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 150 } };
            SEntMan.System<DamageableSystem>().TryChangeDamage(target, damage, ignoreResistances: true);
        });
        await Pair.RunTicksSync(10);

        await StartCarry(carrier, target);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<MobStateComponent>(target).CurrentState, Is.EqualTo(MobState.Critical));
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.True);
        });

        await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
            Assert.That(SEntMan.GetComponent<StandingStateComponent>(target).Standing, Is.False);
        });
    }

    [Test]
    public async Task KnockedDownTargetStaysDownOnRelease()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() => SEntMan.AddComponent<KnockedDownComponent>(target));
        await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(target), Is.True);
            Assert.That(SEntMan.GetComponent<StandingStateComponent>(target).Standing, Is.False);
        });
    }


    [Test]
    public async Task RepeatedCarryLeavesNoAnchorOrVirtualItems()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        var hands = SEntMan.System<SharedHandsSystem>();

        for (var i = 0; i < 2; i++)
        {
            await StartCarry(carrier, target);
            await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
            await Pair.RunTicksSync(10);

            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
                Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
                Assert.That(hands.CountFreeHands(carrier), Is.EqualTo(2));
            });
        }

        var anchors = 0;
        var query = SEntMan.EntityQueryEnumerator<CarryAnchorComponent>();
        while (query.MoveNext(out _, out _))
            anchors++;

        Assert.That(anchors, Is.EqualTo(0), "carry anchors must not accumulate");
    }


    [Test]
    public async Task StunDuringPickupCancelsTheAttempt()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await Server.WaitPost(() => Assert.That(system.TryStartCarry(carrier, target), Is.True));

        await Server.WaitPost(() =>
        {
            var ev = new StunnedEvent();
            SEntMan.EventBus.RaiseLocalEvent(carrier, ref ev);
        });
        await Pair.RunTicksSync(140);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<CarryAttemptComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarryAttemptTargetComponent>(target), Is.False);
        });
    }


    [Test]
    public async Task CarrierCriticalEndsCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() =>
        {
            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 150 } };
            SEntMan.System<DamageableSystem>().TryChangeDamage(carrier, damage, ignoreResistances: true);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<MobStateComponent>(carrier).CurrentState, Is.EqualTo(MobState.Critical));
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });
    }

    [Test]
    public async Task CarrierStunEndsCarryAndCanRecover()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        var stun = Server.System<StunSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() => Assert.That(stun.TryAddStunDuration(carrier, TimeSpan.FromSeconds(1)), Is.True));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
            Assert.That(SEntMan.GetComponent<BuckleComponent>(target).Buckled, Is.False);
        });

        await Pair.RunTicksSync(100);
        await Server.WaitPost(() => Assert.That(system.TryStartCarry(carrier, target), Is.True));
        await Pair.RunTicksSync(140);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.True));
        await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
        await Pair.RunTicksSync(5);
    }

    [Test]
    public async Task CarrierKnockdownEndsCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        var stun = Server.System<StunSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() => Assert.That(
            stun.TryKnockdown(carrier, TimeSpan.FromSeconds(2), force: true), Is.True));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });

        await Server.WaitPost(() => stun.TryStanding(carrier));
        await Pair.RunTicksSync(10);
    }


    [Test]
    public async Task CarrierFallingEndsCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() => SEntMan.EnsureComponent<ChasmFallingComponent>(carrier));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });

        await Server.WaitPost(() => SEntMan.RemoveComponent<ChasmFallingComponent>(carrier));
        await Pair.RunTicksSync(10);
    }

    [Test]
    public async Task CarrierCuffedEndsCarry()
    {
        var (carrier, target, mapId) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() =>
        {
            var cuffs = SEntMan.SpawnEntity("Handcuffs", new MapCoordinates(Vector2.Zero, mapId));
            SEntMan.EnsureComponent<CuffableComponent>(carrier);
            Assert.That(Server.System<CuffableSystem>().TryAddNewCuffs(carrier, carrier, cuffs), Is.True);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });

        await Server.WaitPost(() =>
            Assert.That(system.TryStartCarry(carrier, target), Is.False,
                "a fully cuffed carrier cannot start another carry"));
    }

    [Test]
    public async Task CarrierStunThenKnockdownEndsOnceAndLeavesNoArtifacts()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        var stun = Server.System<StunSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() =>
        {
            Assert.That(stun.TryAddStunDuration(carrier, TimeSpan.FromSeconds(1)), Is.True);
            Assert.That(stun.TryKnockdown(carrier, TimeSpan.FromSeconds(1), force: true), Is.True);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().CountFreeHands(carrier), Is.EqualTo(2));

            var anchors = 0;
            var query = SEntMan.EntityQueryEnumerator<CarryAnchorComponent>();
            while (query.MoveNext(out _, out _))
                anchors++;
            Assert.That(anchors, Is.EqualTo(0));
        });

        await Server.WaitPost(() => stun.TryStanding(carrier));
        await Pair.RunTicksSync(100);
    }

    [Test]
    public async Task CarrierCriticalAndStunDoNotDoubleEndOrLeak()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        var stun = Server.System<StunSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() =>
        {
            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 150 } };
            SEntMan.System<DamageableSystem>().TryChangeDamage(carrier, damage, ignoreResistances: true);
        });
        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False));

        await Server.WaitPost(() => Assert.That(stun.TryAddStunDuration(carrier, TimeSpan.FromSeconds(1)), Is.True));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().CountFreeHands(carrier), Is.EqualTo(2));
        });

        await Pair.RunTicksSync(100);
    }

    [Test]
    public async Task CarrierDeathDuringStunEndsOnceWithoutLeaks()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        var stun = Server.System<StunSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() => Assert.That(stun.TryAddStunDuration(carrier, TimeSpan.FromSeconds(2)), Is.True));
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False));

        await Server.WaitPost(() =>
        {
            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 300 } };
            SEntMan.System<DamageableSystem>().TryChangeDamage(carrier, damage, ignoreResistances: true);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
            Assert.That(SEntMan.GetComponent<BuckleComponent>(target).Buckled, Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().CountFreeHands(carrier), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task TargetStunAloneIsNotForcedToStandOnRelease()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        var stun = Server.System<StunSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() => Assert.That(stun.TryAddStunDuration(target, TimeSpan.FromSeconds(1)), Is.True));
        await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
            Assert.That(SEntMan.GetComponent<StandingStateComponent>(target).Standing, Is.False);
        });

        await Pair.RunTicksSync(100);
    }

    [Test]
    public async Task TargetStunAndKnockdownDoNotEndCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        var stun = Server.System<StunSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() =>
        {
            Assert.That(stun.TryAddStunDuration(target, TimeSpan.FromSeconds(1)), Is.True);
            Assert.That(stun.TryKnockdown(target, TimeSpan.FromSeconds(2), force: true), Is.True);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.True);
            Assert.That(SEntMan.GetComponent<CarriedComponent>(target).Carrier, Is.EqualTo(carrier));
        });

        await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
            Assert.That(SEntMan.GetComponent<StandingStateComponent>(target).Standing, Is.False);
        });
    }

    [Test, PairConfig(nameof(SleepOnlySettings))]
    public async Task CarrierSleepingEndsCarry()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        var sleeping = Server.System<SleepingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitPost(() => Assert.That(sleeping.TrySleeping(carrier), Is.True));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });

        await Server.WaitPost(() => sleeping.TryWaking(carrier, force: true));
        await Pair.RunTicksSync(10);
    }

    [Test]
    public async Task CarrierTeleportKeepsCarryAttached()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        var map2 = MapId.Nullspace;
        await Server.WaitPost(() =>
        {
            var map = Server.System<SharedMapSystem>();
            var mapUid = map.CreateMap(out map2);
            Server.System<Robust.Shared.GameObjects.SharedTransformSystem>()
                .SetCoordinates(carrier, new EntityCoordinates(mapUid, Vector2.Zero));
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.True);
            Assert.That(SEntMan.GetComponent<TransformComponent>(carrier).MapID, Is.EqualTo(map2));
            Assert.That(SEntMan.GetComponent<TransformComponent>(target).MapID, Is.EqualTo(map2));
            Assert.That(SEntMan.GetComponent<CarriedComponent>(target).Carrier, Is.EqualTo(carrier));
        });

        await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
        await Pair.RunTicksSync(5);
    }


    [Test]
    public async Task IpcCanBeCarriedAndReleased()
    {
        var (carrier, target, _) = await CreatePair("MobIpc");
        var system = Server.System<CarryingSystem>();
        await StartCarry(carrier, target);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.True);
            Assert.That(SEntMan.GetComponent<CarriedComponent>(target).Carrier, Is.EqualTo(carrier));
            Assert.That(SEntMan.GetComponent<BuckleComponent>(target).Buckled, Is.True);
        });

        await Server.WaitPost(() => Assert.That(system.TryEndCarry(carrier, carrier), Is.True));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CarryingComponent>(carrier), Is.False);
            Assert.That(SEntMan.HasComponent<CarriedComponent>(target), Is.False);
        });
    }


    [Test]
    public async Task CannotStartCarryWhileEitherParticipantIsClimbing()
    {
        var (carrier, target, _) = await CreatePair();
        var system = Server.System<CarryingSystem>();

        await Server.WaitPost(() =>
            SEntMan.EnsureComponent<ClimbingComponent>(target).IsClimbing = true);
        await Server.WaitPost(() => Assert.That(system.TryStartCarry(carrier, target), Is.False));

        await Server.WaitPost(() =>
        {
            SEntMan.GetComponent<ClimbingComponent>(target).IsClimbing = false;
            SEntMan.EnsureComponent<ClimbingComponent>(carrier).IsClimbing = true;
        });
        await Server.WaitPost(() => Assert.That(system.TryStartCarry(carrier, target), Is.False));
    }

}
