#nullable enable
using System.Linq;
using Content.Client.Eye.Blinking;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
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
[NonParallelizable]
public sealed class DeadBlinkTest : GameTest
{
    // This test asserts client-only eyelid/timer state, so a pair reused after
    // another client test can otherwise start from a stale desynchronised state.
    public static PoolSettings FreshPair => new()
    {
        Connected = true,
        Fresh = true,
        Destructive = true,
    };

    [Test]
    [PairConfig(nameof(FreshPair))]
    public async Task DeadMobKeepsEyesOpenAndResumesAfterRevival()
    {
        EntityUid serverMob = default;
        NetEntity serverNetMob = default;
        var map = await Pair.CreateTestMap();

        await Server.WaitPost(() =>
        {
            serverMob = SSpawnAtPosition("MobHuman", map.GridCoords);
            serverNetMob = SEntMan.GetNetEntity(serverMob);
        });
        await Pair.RunUntilSynced();
        await RunTicksSync(30);

        EntityUid clientEyes = default;
        EyeBlinkingComponent blinking = null!;
        ClientEyeBlinkingSystem clientBlinking = null!;

        // Wait for the client to receive the spawned mob before mapping its entity.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (attempt > 0)
                await RunTicksSync(1);

            await Client.WaitPost(() =>
            {
                clientBlinking ??= Client.System<ClientEyeBlinkingSystem>();

                if (!CEntMan.TryGetEntity(serverNetMob, out var clientMob) || clientMob == null)
                    return;

                var query = CEntMan.EntityQueryEnumerator<EyeBlinkingComponent>();
                while (query.MoveNext(out var uid, out var comp))
                {
                    if (comp.Body != clientMob.Value)
                        continue;

                    clientEyes = uid;
                    blinking = comp;
                    break;
                }
            });

            if (blinking != null)
                break;
        }

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
        NetEntity serverNetMob = default;
        var map = await Pair.CreateTestMap();

        await Server.WaitPost(() =>
        {
            serverMob = SSpawnAtPosition("MobHuman", map.GridCoords);
            serverNetMob = SEntMan.GetNetEntity(serverMob);
        });
        await Pair.RunUntilSynced();
        await RunTicksSync(30);

        EntityUid clientMob = default;
        EyeBlinkingComponent blinking = null!;

        // Wait for the client to receive the spawned mob before mapping its entity.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (attempt > 0)
                await RunTicksSync(1);

            await Client.WaitPost(() =>
            {
                if (!CEntMan.TryGetEntity(serverNetMob, out var mappedMob) || mappedMob == null)
                    return;

                clientMob = mappedMob.Value;

                var query = CEntMan.EntityQueryEnumerator<EyeBlinkingComponent>();
                while (query.MoveNext(out _, out var comp))
                {
                    if (comp.Body != clientMob)
                        continue;

                    blinking = comp;
                    break;
                }
            });

            if (blinking != null)
                break;
        }

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

    [Test]
    public async Task SpawnedCorpseDoesNotBlink()
    {
        EntityUid serverCorpse = default;
        NetEntity serverNetCorpse = default;
        var map = await Pair.CreateTestMap();

        await Server.WaitPost(() =>
        {
            serverCorpse = SSpawnAtPosition("SalvageHumanCorpse", map.GridCoords);
            serverNetCorpse = SEntMan.GetNetEntity(serverCorpse);
        });
        await Pair.RunUntilSynced();
        await RunTicksSync(30);

        await Server.WaitAssertion(() =>
        {
            var mobState = SEntMan.GetComponent<MobStateComponent>(serverCorpse);
            Assert.That(mobState.CurrentState, Is.EqualTo(MobState.Dead),
                "the spawned corpse is not in the Dead mob state");
        });

        EyeBlinkingComponent blinking = null!;

        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (attempt > 0)
                await RunTicksSync(1);

            await Client.WaitPost(() =>
            {
                if (!CEntMan.TryGetEntity(serverNetCorpse, out var clientCorpse) || clientCorpse == null)
                    return;

                var query = CEntMan.EntityQueryEnumerator<EyeBlinkingComponent>();
                while (query.MoveNext(out _, out var comp))
                {
                    if (comp.Body != clientCorpse.Value)
                        continue;

                    blinking = comp;
                    break;
                }
            });

            if (blinking != null)
                break;
        }

        Assert.That(blinking, Is.Not.Null, "the client did not initialize EyeBlinkingComponent for the corpse");
        Assert.That(blinking.Eyelids, Is.Not.Empty, "the client did not initialize eyelid layers for the corpse");

        await Client.WaitAssertion(() =>
        {
            Assert.That(blinking.Status.HasFlag(BlinkStatus.Dead), Is.True,
                "the corpse did not receive the Dead blink status");
            Assert.That(blinking.BlinkInProgress, Is.False);
            Assert.That(blinking.Eyelids.Any(e => e.IsClosed), Is.False, "the corpse spawned with closed eyes");
        });

        await RunTicksSync(300);

        await Client.WaitAssertion(() =>
        {
            Assert.That(blinking.BlinkInProgress, Is.False, "the corpse started a blink");
            Assert.That(blinking.Eyelids.Any(e => e.IsClosed), Is.False, "the corpse blinked");
        });

        await Server.WaitPost(() => SDeleteNow(serverCorpse));
        await RunTicksSync(10);
    }
}
