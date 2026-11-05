#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Buckle;
using ContentDrawDepth = Content.Shared.DrawDepth.DrawDepth;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using ClientBuckleSystem = Content.Client.Buckle.BuckleSystem;

namespace Content.IntegrationTests.Tests._VanGuard.Buckle;

/// <summary>
///     Characters lie on top of beds and rollerbeds, and blankets render above them.
/// </summary>
[TestFixture]
public sealed class BedPerspectiveSortingTest : InteractionTest
{
    [TestCase("Bed")]
    [TestCase("RollerBed")]
    public async Task LyingCharacterStaysAboveBed(string bedPrototype)
    {
        EntityUid bed = default;
        EntityUid mob = default;

        await Client.WaitPost(() => Client.Resolve<IEyeManager>().CurrentEye.Rotation = Angle.Zero);

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(PlayerCoords);
            bed = SSpawnAtPosition(bedPrototype, coords);
            mob = SSpawnAtPosition("MobHuman", coords);

            Assert.That(SEntMan.System<Content.Server.Buckle.Systems.BuckleSystem>()
                .TryBuckle(mob, SPlayer, bed), Is.True, "the mob should be buckled to the bed");
        });

        await RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var bedSprite = CEntMan.GetComponent<SpriteComponent>(ToClientUid(bed));
            var mobSprite = CEntMan.GetComponent<SpriteComponent>(ToClientUid(mob));
            Assert.That(mobSprite.DrawDepth, Is.EqualTo((int) ContentDrawDepth.Mobs),
                "the character should stay above the bed");
            Assert.That(mobSprite.DrawDepth, Is.GreaterThan(bedSprite.DrawDepth),
                "the bed should not overlap the character");
        });

        // Rotate the camera: the lying-strap rule must not turn into chair occlusion.
        await Client.WaitPost(() =>
        {
            Client.Resolve<IEyeManager>().CurrentEye.Rotation = new Angle(Math.PI);
            Client.System<ClientBuckleSystem>().Update(0);
        });

        await Client.WaitAssertion(() =>
        {
            var bedSprite = CEntMan.GetComponent<SpriteComponent>(ToClientUid(bed));
            var mobSprite = CEntMan.GetComponent<SpriteComponent>(ToClientUid(mob));
            Assert.That(mobSprite.DrawDepth, Is.EqualTo((int) ContentDrawDepth.Mobs),
                "camera rotation should not put the character behind the bed");
            Assert.That(mobSprite.DrawDepth, Is.GreaterThan(bedSprite.DrawDepth),
                "the bed should still not overlap the character");
        });
    }

    [Test]
    public async Task BlanketRendersAboveCharacter()
    {
        EntityUid mob = default;
        EntityUid blanket = default;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(PlayerCoords);
            mob = SSpawnAtPosition("MobHuman", coords);
            blanket = SSpawnAtPosition("BedsheetBlue", coords);
        });

        await RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var mobSprite = CEntMan.GetComponent<SpriteComponent>(ToClientUid(mob));
            var blanketSprite = CEntMan.GetComponent<SpriteComponent>(ToClientUid(blanket));

            Assert.That(blanketSprite.DrawDepth, Is.EqualTo((int) ContentDrawDepth.OverMobs),
                "a bedsheet should render above mobs");
            Assert.That(blanketSprite.DrawDepth, Is.GreaterThan(mobSprite.DrawDepth),
                "the blanket should render above the character");
        });
    }
}
