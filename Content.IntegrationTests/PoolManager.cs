#nullable enable
using System.Linq;
using System.Reflection;
using Content.IntegrationTests.Pair;
using NUnit.Framework;
using Content.Shared.CCVar;
using Robust.UnitTesting;

namespace Content.IntegrationTests;

// The static class exist to avoid breaking changes
public static partial class PoolManager
{
    public static readonly ContentPoolManager Instance = new();
    public const string TestMap = "Empty";

    /// <summary>
    /// Designated load bearing station. Sometimes you need a station for a test.
    /// </summary>
    public const string TestStation = "Saltern";

    /// <summary>
    /// Runs a server, or a client until a condition is true
    /// </summary>
    /// <param name="instance">The server or client</param>
    /// <param name="func">The condition to check</param>
    /// <param name="maxTicks">How many ticks to try before giving up</param>
    /// <param name="tickStep">How many ticks to wait between checks</param>
    public static async Task WaitUntil(RobustIntegrationTest.IntegrationInstance instance, Func<bool> func,
        int maxTicks = 600,
        int tickStep = 1)
    {
        await WaitUntil(instance, async () => await Task.FromResult(func()), maxTicks, tickStep);
    }

    /// <summary>
    /// Runs a server, or a client until a condition is true
    /// </summary>
    /// <param name="instance">The server or client</param>
    /// <param name="func">The async condition to check</param>
    /// <param name="maxTicks">How many ticks to try before giving up</param>
    /// <param name="tickStep">How many ticks to wait between checks</param>
    public static async Task WaitUntil(RobustIntegrationTest.IntegrationInstance instance, Func<Task<bool>> func,
        int maxTicks = 600,
        int tickStep = 1)
    {
        var ticksAwaited = 0;
        bool passed;

        await instance.WaitIdleAsync();

        while (!(passed = await func()) && ticksAwaited < maxTicks)
        {
            var ticksToRun = tickStep;

            if (ticksAwaited + tickStep > maxTicks)
            {
                ticksToRun = maxTicks - ticksAwaited;
            }

            await instance.WaitRunTicks(ticksToRun);

            ticksAwaited += ticksToRun;
        }

        if (!passed)
        {
            Assert.Fail($"Condition did not pass after {maxTicks} ticks.\n" +
                        $"Tests ran ({instance.TestsRan.Count}):\n" +
                        $"{string.Join('\n', instance.TestsRan)}");
        }

        Assert.That(passed);
    }

    public static async Task<TestPair> GetServerClient(
        PoolSettings? settings = null,
        ITestContextLike? testContext = null)
    {
        try
        {
            return await Instance.GetPair(settings, testContext);
        }
        catch (AssertionException) when (settings is { Connected: true, InLobby: false })
        {
            // A pooled pair can be returned while its test session is detached
            // (for example after a command test that ghosts the player). Do not
            // fail the next test on that stale pair: create a fresh one.
            return await Instance.GetPair(CreateFresh(settings), testContext);
        }
        catch (InvalidOperationException) when (settings is { Connected: true, InLobby: false })
        {
            return await Instance.GetPair(CreateFresh(settings), testContext);
        }
    }

    private static PoolSettings CreateFresh(PoolSettings settings)
    {
        return new PoolSettings
        {
            Connected = settings.Connected,
            Destructive = settings.Destructive,
            DisableInterpolate = settings.DisableInterpolate,
            Dirty = settings.Dirty,
            Fresh = true,
            InLobby = settings.InLobby,
            AdminLogsEnabled = settings.AdminLogsEnabled,
            NoLoadContent = settings.NoLoadContent,
            Map = settings.Map,
            DummyTicker = settings.DummyTicker,
            ServerSeed = settings.ServerSeed,
            ClientSeed = settings.ClientSeed,
        };
    }

    public static void Startup(params Assembly[] extra)
        => Instance.Startup(extra);

    public static void Shutdown() => Instance.Shutdown();
    public static string DeathReport() => Instance.DeathReport();
}

/// <summary>
/// Making clients, and servers is slow, this manages a pool of them so tests can reuse them.
/// </summary>
public sealed class ContentPoolManager : PoolManager<TestPair>
{
    public override PairSettings DefaultSettings =>  new PoolSettings();
    protected override string GetDefaultTestName(ITestContextLike testContext)
    {
        return testContext.FullName.Replace("Content.IntegrationTests.Tests.", "");
    }

    public override void Startup(params Assembly[] extraAssemblies)
    {
        DefaultCvars.AddRange(PoolManager.TestCvars);

        var shared = extraAssemblies
                .Append(typeof(Shared.Entry.EntryPoint).Assembly)
                .Append(typeof(PoolManager).Assembly)
                .ToArray();

        Startup([typeof(Client.Entry.EntryPoint).Assembly],
            [typeof(Server.Entry.EntryPoint).Assembly],
            shared);
    }
}
