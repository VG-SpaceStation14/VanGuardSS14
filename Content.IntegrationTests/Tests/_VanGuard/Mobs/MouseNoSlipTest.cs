using Content.IntegrationTests.Fixtures;
using Content.Shared.Slippery;

namespace Content.IntegrationTests.Tests._VanGuard.Mobs;

/// <summary>
///     small critters like mice are not supposed to slip on puddles, banana peels and
///     other slippery surfaces - they are too small for that to make any sense.
/// </summary>
[TestFixture]
[TestOf(typeof(NoSlipComponent))]
public sealed class MouseNoSlipTest : GameTest
{
    [Test]
    [TestCase("MobMouse")]
    [TestCase("MobMouse1")]
    [TestCase("MobMouse2")]
    [TestCase("MobMouseCancer")]
    [Description("Mice should reject slip attempts instead of falling over with a sound.")]
    public async Task MouseRejectsSlipAttempts(string mouseProto)
    {
        await Server.WaitAssertion(() =>
        {
            var mouse = SSpawn(mouseProto);

            Assert.That(SEntMan.HasComponent<NoSlipComponent>(mouse), Is.True,
                $"{mouseProto} should have a NoSlip component");

            var ev = new SlipAttemptEvent(null);
            SEntMan.EventBus.RaiseLocalEvent(mouse, ev);

            Assert.That(ev.NoSlip, Is.True,
                $"{mouseProto} should not be knocked down by slippery surfaces");
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
