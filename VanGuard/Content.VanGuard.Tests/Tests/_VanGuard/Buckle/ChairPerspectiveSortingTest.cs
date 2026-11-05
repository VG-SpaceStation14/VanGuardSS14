#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using ContentDrawDepth = Content.Shared.DrawDepth.DrawDepth;
using Content.Shared.Movement.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using ClientBuckleSystem = Content.Client.Buckle.BuckleSystem;

namespace Content.IntegrationTests.Tests._VanGuard.Buckle;

/// <summary>
///     Verifies that a buckled chair is sorted relative to the camera direction and
///     that normal entities keep their draw depth.
/// </summary>
[TestFixture]
public sealed class ChairPerspectiveSortingTest : InteractionTest
{
    [Test]
    public async Task ChairOcclusionTracksCameraRotation()
    {
        EntityUid chair = default;
        EntityUid mob = default;
        EntityUid bystander = default;

        await Client.WaitPost(() => Client.Resolve<IEyeManager>().CurrentEye.Rotation = Angle.Zero);

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(PlayerCoords);
            chair = SSpawnAtPosition("Chair", coords);
            mob = SSpawnAtPosition("MobHuman", coords);
            bystander = SSpawnAtPosition("MobHuman", coords.Offset(new Vector2(1f, 0f)));
        });

        await RunTicksSync(10);

        await Server.WaitPost(() =>
        {
            // North facing chair: its backrest is between the camera and the seated mob.
            SEntMan.System<SharedTransformSystem>().SetWorldRotation(chair, new Angle(Math.PI));

            Assert.That(SEntMan.System<Content.Server.Buckle.Systems.BuckleSystem>()
                .TryBuckle(mob, SPlayer, chair), Is.True, "the mob should be buckled to the chair");
        });

        await RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var clientChair = ToClientUid(chair);
            var clientMob = ToClientUid(mob);
            var chairSprite = CEntMan.GetComponent<SpriteComponent>(clientChair);
            var mobSprite = CEntMan.GetComponent<SpriteComponent>(clientMob);
            var strap = CEntMan.GetComponent<StrapComponent>(clientChair);
            var buckle = CEntMan.GetComponent<BuckleComponent>(clientMob);
            var bystanderSprite = CEntMan.GetComponent<SpriteComponent>(ToClientUid(bystander));

            Assert.Multiple(() =>
            {
                Assert.That(mobSprite.DrawDepth, Is.EqualTo(chairSprite.DrawDepth - 1),
                    "the chair should render above the seated mob when its backrest faces the camera");
                Assert.That(chairSprite.DrawDepth, Is.EqualTo((int) ContentDrawDepth.Objects),
                    "the chair should keep its normal draw depth");
                Assert.That(buckle.Buckled, Is.True);
                Assert.That(strap.BuckledEntities, Has.Count.EqualTo(1));
                Assert.That(bystanderSprite.DrawDepth, Is.EqualTo((int) ContentDrawDepth.Mobs),
                    "an unrelated mob should keep its normal draw depth");
            });
        });

        // Rotate the camera to a side view: the backrest no longer occludes the mob.
        await Client.WaitPost(() =>
        {
            Client.Resolve<IEyeManager>().CurrentEye.Rotation = new Angle(Math.PI / 2);
            Client.System<ClientBuckleSystem>().Update(0);
        });

        await Client.WaitAssertion(() =>
        {
            var mobSprite = CEntMan.GetComponent<SpriteComponent>(ToClientUid(mob));
            Assert.That(mobSprite.DrawDepth, Is.EqualTo((int) ContentDrawDepth.Mobs),
                "the seated mob should render above the chair in a side view");
        });

        // Rotate the camera so the chair is now behind the seated mob.
        await Client.WaitPost(() =>
        {
            Client.Resolve<IEyeManager>().CurrentEye.Rotation = new Angle(Math.PI);
            Client.System<ClientBuckleSystem>().Update(0);
        });

        await Client.WaitAssertion(() =>
        {
            var mobSprite = CEntMan.GetComponent<SpriteComponent>(ToClientUid(mob));
            Assert.That(mobSprite.DrawDepth, Is.EqualTo((int) ContentDrawDepth.Mobs),
                "the seated mob should render above the chair when the camera is on the other side");
        });

        // Rotate back: the chair must occlude the mob again.
        await Client.WaitPost(() =>
        {
            Client.Resolve<IEyeManager>().CurrentEye.Rotation = Angle.Zero;
            Client.System<ClientBuckleSystem>().Update(0);
        });

        await Client.WaitAssertion(() =>
        {
            var chairSprite = CEntMan.GetComponent<SpriteComponent>(ToClientUid(chair));
            var mobSprite = CEntMan.GetComponent<SpriteComponent>(ToClientUid(mob));
            Assert.That(mobSprite.DrawDepth, Is.EqualTo(chairSprite.DrawDepth - 1),
                "the chair should occlude the mob again after rotating the camera back");
        });

        await Server.WaitPost(() => SEntMan.System<Content.Server.Buckle.Systems.BuckleSystem>()
            .TryUnbuckle(mob, null, popup: true));
        await RunTicksSync(5);

        await Client.WaitPost(() =>
        {
            var clientChair = ToClientUid(chair);
            var clientMob = ToClientUid(mob);
            if (!CEntMan.TryGetComponent<BuckleComponent>(clientMob, out var buckle))
                return;

            var strap = CEntMan.GetComponent<StrapComponent>(clientChair);
            var ev = new UnbuckledEvent((clientChair, strap), (clientMob, buckle));
            CEntMan.EventBus.RaiseLocalEvent(clientMob, ref ev);
        });

        await Client.WaitAssertion(() =>
        {
            var mobSprite = CEntMan.GetComponent<SpriteComponent>(ToClientUid(mob));
            Assert.That(mobSprite.DrawDepth, Is.EqualTo((int) ContentDrawDepth.Mobs),
                "unbuckling should restore the mob's original draw depth");
        });
    }

    [Test]
    public async Task ChairBehindSeatedMobDoesNotOverrideDrawDepth()
    {
        EntityUid chair = default;
        EntityUid mob = default;

        await Client.WaitPost(() => Client.Resolve<IEyeManager>().CurrentEye.Rotation = Angle.Zero);

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(PlayerCoords);
            chair = SSpawnAtPosition("Chair", coords);
            mob = SSpawnAtPosition("MobHuman", coords);

            // The chair faces south on screen, so its backrest is not between the camera and the mob.
            SEntMan.System<SharedTransformSystem>().SetWorldRotation(chair, Angle.Zero);

            Assert.That(SEntMan.System<Content.Server.Buckle.Systems.BuckleSystem>()
                .TryBuckle(mob, SPlayer, chair), Is.True, "the mob should be buckled to the chair");
        });

        await RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var mobSprite = CEntMan.GetComponent<SpriteComponent>(ToClientUid(mob));
            Assert.That(mobSprite.DrawDepth, Is.EqualTo((int) ContentDrawDepth.Mobs),
                "the seated mob should stay above a chair that is not in front of it");
        });
    }
}
