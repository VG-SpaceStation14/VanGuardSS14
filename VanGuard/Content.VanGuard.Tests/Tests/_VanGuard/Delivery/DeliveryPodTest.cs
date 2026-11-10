#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._VanGuard.Delivery;
using Content.Shared._VanGuard.Delivery.Components;
using Content.IntegrationTests.Tests.Interaction;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests._VanGuard.Delivery;

[TestFixture]
public sealed class DeliveryPodTest : InteractionTest
{
    [Test]
    public async Task PodKeepsGoodsUntilLandingAndOpensOnce()
    {
        EntityUid pod = default;
        EntityUid openedPod = default;
        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            var system = Server.System<DeliveryPodSystem>();
            Assert.That(system.TryCreateDelivery(
                coords,
                new[] { new EntProtoId("Flare"), new EntProtoId("Pickaxe") },
                "DeliveryPod"), Is.True);

            var query = SEntMan.EntityQueryEnumerator<DeliveryPodComponent>();
            Assert.That(query.MoveNext(out var uid, out var delivery), Is.True);
            pod = uid;

            Assert.That(delivery!.Contents.ContainedEntities, Has.Count.EqualTo(2));
            Assert.That(delivery.Landed, Is.False);
        });

        await Pair.RunTicksSync(30);
        var clientPod = ToClientUid(pod);
        await Client.WaitAssertion(() =>
        {
            var sprite = CEntMan.GetComponent<SpriteComponent>(clientPod);
            Assert.That(sprite.LayerData[0].State.ToString(), Is.EqualTo("dropping"),
                "the falling pod visual must be used");
        });

        await Server.WaitAssertion(() =>
        {
            var delivery = SEntMan.GetComponent<DeliveryPodComponent>(pod);
            Assert.That(delivery.Landed, Is.False);
            Assert.That(delivery.Contents.ContainedEntities, Has.Count.EqualTo(2));
        });

        await Pair.RunTicksSync(150);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(pod), Is.False,
                "the falling pod must be replaced after its 5.1 second lifetime");
            Assert.That(FloorCount("Flare"), Is.EqualTo(1));
            Assert.That(FloorCount("Pickaxe"), Is.EqualTo(1));

            var opened = FindOpenedPod();
            Assert.That(opened, Is.Not.EqualTo(EntityUid.Invalid),
                "a separate opened pod/crater entity must be spawned");
            openedPod = opened;
            Assert.That(SEntMan.GetComponent<TimedDespawnComponent>(opened).Lifetime, Is.LessThanOrEqualTo(16f));
        });

        await Pair.RunTicksSync(2);
        var clientOpened = ToClientUid(openedPod);
        await Client.WaitAssertion(() =>
        {
            var sprite = CEntMan.GetComponent<SpriteComponent>(clientOpened);
            Assert.That(sprite.LayerData[0].State.ToString(), Is.EqualTo("droppod_opened"),
                "the opened pod/crater visual must be used");
        });

        await Pair.RunTicksSync(30);
        await AssertFloorCount("Flare", 1);
        await AssertFloorCount("Pickaxe", 1);
    }

    [Test]
    public async Task DeletingPodBeforeLandingSpillsGoodsOnce()
    {
        EntityUid pod = default;
        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            var system = Server.System<DeliveryPodSystem>();
            Assert.That(system.TryCreateDelivery(
                coords,
                new[] { new EntProtoId("Flare") },
                "DeliveryPod"), Is.True);

            var query = SEntMan.EntityQueryEnumerator<DeliveryPodComponent>();
            Assert.That(query.MoveNext(out var uid, out _), Is.True);
            pod = uid;
        });

        await Server.WaitPost(() => SEntMan.DeleteEntity(pod));
        await Pair.RunTicksSync(5);

        await AssertFloorCount("Flare", 1);

        await Pair.RunTicksSync(30);
        await AssertFloorCount("Flare", 1);
    }

    [Test]
    public async Task InvalidContentsDoNotCreatePod()
    {
        var before = 0;
        var created = true;
        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<DeliveryPodComponent>();
            while (query.MoveNext(out _, out _))
                before++;

            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            created = Server.System<DeliveryPodSystem>().TryCreateDelivery(
                coords,
                new[] { new EntProtoId("ThisPrototypeDoesNotExist") },
                "DeliveryPod");
        });

        Assert.That(created, Is.False, "invalid contents must make creation fail");
        await Server.WaitAssertion(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<DeliveryPodComponent>();
            var after = 0;
            while (query.MoveNext(out _, out _))
                after++;
            Assert.That(after, Is.EqualTo(before), "a failed delivery must not leave a pod behind");
        });
    }

    private EntityUid FindOpenedPod()
    {
        EntityUid found = default;
        var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype?.ID == "DeliveryPodOpened")
                found = uid;
        }

        return found;
    }

    private int FloorCount(string prototype)
    {
        var count = 0;
        var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == prototype)
                count++;
        }

        return count;
    }

    private async Task AssertFloorCount(string prototype, int expected)
    {
        await Server.WaitAssertion(() =>
            Assert.That(FloorCount(prototype), Is.EqualTo(expected),
                $"{prototype} should exist exactly {expected} time(s)"));
    }
}
