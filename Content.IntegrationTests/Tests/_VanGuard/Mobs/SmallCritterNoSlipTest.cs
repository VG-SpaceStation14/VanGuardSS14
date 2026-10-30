using Content.IntegrationTests.Fixtures;
using Content.Shared.Slippery;

namespace Content.IntegrationTests.Tests._VanGuard.Mobs;

/// <summary>
///     VG-Tweak: small critters (mice, cockroaches, mothroaches, lizards, snails, slugs and their variants) are not
///     supposed to slip on puddles, banana peels and other slippery surfaces - they are too small for that to make
///     any sense.
/// </summary>
[TestFixture]
[TestOf(typeof(NoSlipComponent))]
public sealed class SmallCritterNoSlipTest : GameTest
{
    [Test]
    [TestCase("MobMouse")]
    [TestCase("MobMouse1")]
    [TestCase("MobMouse2")]
    [TestCase("MobMouseCancer")]
    [TestCase("MobCockroach")]
    [TestCase("MobGlockroach")]
    [TestCase("MobMothroach")]
    [TestCase("MobMoproach")]
    [TestCase("MobLizard")]
    [TestCase("MobSnail")]
    [TestCase("MobSnailSpeed")]
    [TestCase("MobSnailMoth")]
    [TestCase("MobSlug")]
    [Description("Small critters should reject slip attempts instead of falling over with a sound.")]
    public async Task CritterRejectsSlipAttempts(string critterProto)
    {
        await Server.WaitAssertion(() =>
        {
            var critter = SSpawn(critterProto);

            Assert.That(SEntMan.HasComponent<NoSlipComponent>(critter), Is.True,
                $"{critterProto} should have a NoSlip component");

            var ev = new SlipAttemptEvent(null);
            SEntMan.EventBus.RaiseLocalEvent(critter, ev);

            Assert.That(ev.NoSlip, Is.True,
                $"{critterProto} should not be knocked down by slippery surfaces");
        });
    }

    [Test]
    [Description("Sanity check that regular mobs still slip, so the NoSlip component is not applied globally.")]
    public async Task HumanStillSlips()
    {
        await Server.WaitAssertion(() =>
        {
            var human = SSpawn("MobHuman");

            var ev = new SlipAttemptEvent(null);
            SEntMan.EventBus.RaiseLocalEvent(human, ev);

            Assert.That(ev.NoSlip, Is.False, "Humans should still be able to slip");
        });
    }
}
