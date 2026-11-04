using System.Reflection;
using Robust.UnitTesting.Pool;

[SetUpFixture]
public sealed class PoolManagerTestEventHandler
{
    private static TimeSpan MaximumTotalTestingTimeLimit => TimeSpan.FromMinutes(30);
    private static TimeSpan HardStopTimeLimit => MaximumTotalTestingTimeLimit.Add(TimeSpan.FromMinutes(1));

    [OneTimeSetUp]
    public void Setup()
    {
        // Register [TestPrototypes] from this assembly without loading the
        // whole test assembly into the game server/client.
        var assembly = typeof(PoolManagerTestEventHandler).Assembly;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        foreach (var type in assembly.GetTypes())
        {
            foreach (var field in type.GetFields(flags))
            {
                if (field.GetCustomAttribute<TestPrototypesAttribute>() is null)
                    continue;

                if (field.GetValue(null) is string prototypes)
                    Content.IntegrationTests.PoolManager.Instance.TestPrototypes.Add(prototypes);
            }
        }

        PoolManager.Startup();
        _ = Task.Delay(MaximumTotalTestingTimeLimit).ContinueWith(_ =>
        {
            TestContext.Error.WriteLine($"\n\n{nameof(PoolManagerTestEventHandler)}: ERROR: Tests are taking too long. Shutting down.\n\n");
            PoolManager.Shutdown();
        });

        _ = Task.Delay(HardStopTimeLimit).ContinueWith(_ =>
        {
            var deathReport = PoolManager.DeathReport();
            Environment.FailFast($"Tests took too long;\n Death Report:\n{deathReport}");
        });
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        PoolManager.Shutdown();
    }
}
