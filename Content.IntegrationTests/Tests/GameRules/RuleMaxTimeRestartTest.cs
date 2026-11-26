#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.GameRules;

[TestOf(typeof(MaxTimeRestartRuleSystem))]
public sealed class RuleMaxTimeRestartTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        InLobby = true
    };

    private static readonly EntProtoId MaxTimeRestartGameRule = "MaxTimeRestart";

    [SidedDependency(Side.Server)] private ServerGameTicker _sGameTicker = default!;

    [Test]
    public async Task RestartTest()
    {
        // VG-Tweak Start: Don't assert exact component counts - the pooled pair may have had a
        // preset (e.g. Extended) voted in during recycling, which adds its own rules.
        // Instead we track our specific rule entity below.
        var initialGameRuleCount = SEntMan.Count<GameRuleComponent>();
        var initialActiveGameRuleCount = SEntMan.Count<ActiveGameRuleComponent>();
        if (initialGameRuleCount != 0)
            Assert.Warn($"Initial GameRuleComponent count is {initialGameRuleCount} instead of 0. Previous test may not have cleaned up.");
        if (initialActiveGameRuleCount != 0)
            Assert.Warn($"Initial ActiveGameRuleComponent count is {initialActiveGameRuleCount} instead of 0. Previous test may not have cleaned up.");
        // VG-Tweak End

        MaxTimeRestartRuleComponent? maxTime = null!;
        EntityUid maxTimeRuleUid = EntityUid.Invalid; // VG-Tweak
        await Server.WaitPost(() =>
        {
            _sGameTicker.StartGameRule(MaxTimeRestartGameRule, out var ruleEntity);
            Assert.That(STryComp(ruleEntity?.Owner, out maxTime));
            maxTimeRuleUid = ruleEntity!.Value.Owner; // VG-Tweak
        });

        // VG-Tweak Start: Check that our specific rule exists, not total count.
        Assert.That(maxTimeRuleUid, Is.Not.EqualTo(EntityUid.Invalid), "MaxTimeRestart rule should be created");
        Assert.That(SEntMan.EntityExists(maxTimeRuleUid), "MaxTimeRestart rule entity should exist");
        // VG-Tweak End

        await Server.WaitAssertion(() =>
        {
            Assert.That(_sGameTicker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
            maxTime.RoundMaxTime = TimeSpan.FromSeconds(3);
            _sGameTicker.StartRound();
        });

        // VG-Tweak Start: Verify our rule is still active, not total count.
        Assert.That(SEntMan.EntityExists(maxTimeRuleUid), "MaxTimeRestart rule should still exist after round start");
        Assert.That(SEntMan.HasComponent<ActiveGameRuleComponent>(maxTimeRuleUid), "MaxTimeRestart rule should be active");
        // VG-Tweak End

        await Server.WaitAssertion(() =>
        {
            Assert.That(_sGameTicker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
        });

        var ticks = Server.Timing.TickRate * (int)Math.Ceiling(maxTime.RoundMaxTime.TotalSeconds * 1.1f);
        await RunTicksSync(ticks);

        await Server.WaitAssertion(() =>
        {
            Assert.That(_sGameTicker.RunLevel, Is.EqualTo(GameRunLevel.PostRound));
        });

        ticks = Server.Timing.TickRate * (int)Math.Ceiling(maxTime.RoundEndDelay.TotalSeconds * 1.1f);
        await RunTicksSync(ticks);

        await Server.WaitAssertion(() =>
        {
            Assert.That(_sGameTicker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
        });
    }
}