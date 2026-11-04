#nullable enable
using System.Linq;
using Content.Client.Eye.Blinking;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Eye.Blinking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._VanGuard.Eyes;

/// <summary>
///     Dead entities must keep their eyes open, including when death interrupts an active blink,
///     and blinking must resume after revival.
/// </summary>
[TestFixture]
public sealed class DeadBlinkTest : GameTest
{
    [Test]
    public async Task DeadMobKeepsEyesOpenAndResumesAfterRevival()
    {
        EntityUid serverMob = default;
        var map = await Pair.CreateTestMap();

        await Server.WaitPost(() => serverMob = SSpawnAtPosition("MobHuman", map.GridCoords));
        await RunTicksSync(10);

        EntityUid clientEyes = default;
        EyeBlinkingComponent blinking = null!;
        ClientEyeBlinkingSystem clientBlinking = null!;

        await Client.WaitPost(() =>
        {
            var clientMob = ToClientUid(serverMob);

            var query = CEntMan.EntityQueryEnumerator<EyeBlinkingComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                if (comp.Body != clientMob)
                    continue;

                clientEyes = uid;
                blinking = comp;
                break;
            }

            clientBlinking = Client.System<ClientEyeBlinkingSystem>();
        });

        Assert.That(blinking, Is.Not.Null, "the client did not initialize EyeBlinkingComponent");
        Assert.That(blinking.Eyelids, Is.Not.Empty, "the client did not initialize eyelid layers");

        // The first blink after initialization only clears the initial open state. The next one closes the eyes.
        await Client.WaitAssertion(() =>
        {
            Assert.That(blinking.Status, Is.EqualTo(BlinkStatus.Normal));

            blinking.MaxAsyncBlink = TimeSpan.Zero;
            blinking.MaxAsyncOpenBlink = TimeSpan.Zero;
            blinking.SkipBlinkChance = 0f;
            blinking.LongBlinkChance = 0f;
            blinking.NextBlinkingTime = TimeSpan.MaxValue;

            clientBlinking.Blink((clientEyes, blinking));
        });

        await RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(blinking.BlinkInProgress, Is.False, "the initial open state was not cleared");

            clientBlinking.Blink((clientEyes, blinking));
            Assert.That(blinking.BlinkInProgress, Is.True, "a living entity did not start a blink");
        });

        await RunTicksSync(2);
        await Client.WaitAssertion(() =>
            Assert.That(blinking.Eyelids.Any(e => e.IsClosed), Is.True, "a living entity did not close its eyes"));

        // Death during a blink must immediately open the eyes and cancel the blink.
        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(serverMob, MobState.Dead));
        await RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(blinking.Status.HasFlag(BlinkStatus.Dead), Is.True,
                "the client did not receive the Dead blink status");
            Assert.That(blinking.BlinkInProgress, Is.False, "the blink was not cancelled on death");
            Assert.That(blinking.Eyelids.Any(e => e.IsClosed), Is.False, "the eyes stayed closed after death");
        });

        // Delayed blink requests and timers must not close the eyes of a dead entity.
        await Client.WaitPost(() => clientBlinking.Blink((clientEyes, blinking)));
        await RunTicksSync(300);

        await Client.WaitAssertion(() =>
        {
            Assert.That(blinking.BlinkInProgress, Is.False, "a dead entity started a new blink");
            Assert.That(blinking.Eyelids.Any(e => e.IsClosed), Is.False, "a dead entity closed its eyes");
        });

        // Revival must restore the normal blinking state.
        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(serverMob, MobState.Alive));
        await RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(blinking.Status, Is.EqualTo(BlinkStatus.Normal));
            Assert.That(blinking.BlinkInProgress, Is.False);
            Assert.That(blinking.Eyelids.Any(e => e.IsClosed), Is.False);

            blinking.MaxAsyncBlink = TimeSpan.Zero;
            blinking.MaxAsyncOpenBlink = TimeSpan.Zero;
            blinking.SkipBlinkChance = 0f;
            blinking.LongBlinkChance = 0f;

            clientBlinking.Blink((clientEyes, blinking));
            Assert.That(blinking.BlinkInProgress, Is.True, "the revived entity did not start a blink");
        });

        await RunTicksSync(2);
        await Client.WaitAssertion(() =>
            Assert.That(blinking.Eyelids.Any(e => e.IsClosed), Is.True, "the revived entity did not close its eyes"));

        // Deleting the entity must not leave errors from pending eyelid timers.
        await Server.WaitPost(() => SDeleteNow(serverMob));
        await RunTicksSync(10);
    }

    [Test]
    public async Task DeadMobWithOpenEyesStaysOpenAfterRepeatedDeathEvent()
    {
        EntityUid serverMob = default;
        var map = await Pair.CreateTestMap();

        await Server.WaitPost(() => serverMob = SSpawnAtPosition("MobHuman", map.GridCoords));
        await RunTicksSync(10);

        EntityUid clientMob = default;
        EyeBlinkingComponent blinking = null!;

        await Client.WaitPost(() =>
        {
            clientMob = ToClientUid(serverMob);

            var query = CEntMan.EntityQueryEnumerator<EyeBlinkingComponent>();
            while (query.MoveNext(out _, out var comp))
            {
                if (comp.Body != clientMob)
                    continue;

                blinking = comp;
                break;
            }
        });

        Assert.That(blinking, Is.Not.Null, "the client did not initialize EyeBlinkingComponent");
        Assert.That(blinking.Eyelids, Is.Not.Empty, "the client did not initialize eyelid layers");
        Assert.That(blinking.Eyelids.Any(e => e.IsClosed), Is.False, "the living entity started with closed eyes");

        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(serverMob, MobState.Dead));
        await RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(blinking.Status.HasFlag(BlinkStatus.Dead), Is.True);
            Assert.That(blinking.Eyelids.Any(e => e.IsClosed), Is.False, "the eyes closed after death");
        });

        // A repeated death event must not restore blinking or close the eyes again.
        await Client.WaitPost(() =>
        {
            var mobState = CEntMan.GetComponent<MobStateComponent>(clientMob);
            var ev = new MobStateChangedEvent(clientMob, mobState, MobState.Dead, MobState.Dead);
            CEntMan.EventBus.RaiseLocalEvent(clientMob, ev, true);
        });

        await RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(blinking.BlinkInProgress, Is.False);
            Assert.That(blinking.Eyelids.Any(e => e.IsClosed), Is.False, "a repeated death event closed the eyes");
        });

        await Server.WaitPost(() => SDeleteNow(serverMob));
        await RunTicksSync(10);
    }
}
