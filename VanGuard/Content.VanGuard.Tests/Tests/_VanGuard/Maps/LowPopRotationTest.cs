#nullable enable
using System.Collections.Generic;
using Content.Server.GameTicking;
using Content.Shared.GameTicking.Prototypes;
using Content.Shared.Maps;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Maps;

/// <summary>
///     Locks down the low-pop map rotation and mode thresholds so a pool edit cannot silently
///     remove a playable range or reintroduce a high-pop map into the low-pop rotation.
/// </summary>
[TestFixture]
public sealed class LowPopRotationTest : GameTest
{
    private static readonly ProtoId<GameMapPoolPrototype> VanGuardPool = "VanGuardMapPool";
    private static readonly ProtoId<GameMapPrototype> PackedMap = "VG_Packed";
    private static readonly ProtoId<GameMapPrototype> SillyMap = "VG_Silly";
    private static readonly ProtoId<GameMapPrototype> SalternMap = "VG_Saltern";
    private static readonly ProtoId<GameMapPrototype> ClusterMap = "VG_Cluster";
    private static readonly ProtoId<GameMapPrototype> TushkanMap = "VG_Tushkan";
    private static readonly ProtoId<GameMapPrototype> MarathonMap = "VG_Marathon";
    private static readonly ProtoId<GamePresetPrototype> TraitorPreset = "Traitor";
    private static readonly ProtoId<GamePresetPrototype> WizardPreset = "Wizard";
    private static readonly ProtoId<GamePresetPrototype> RevolutionaryPreset = "Revolutionary";
    private static readonly ProtoId<GamePresetPrototype> NukeopsPreset = "Nukeops";
    private static readonly ProtoId<GamePresetPrototype> ZombiePreset = "Zombie";

    [Test]
    public async Task PoolsAndPresetThresholdsMatchLowPopDesign()
    {
        await Server.WaitAssertion(() =>
        {
            var expected = new HashSet<ProtoId<GameMapPrototype>>
            {
                PackedMap,
                SillyMap,
                SalternMap,
                ClusterMap,
                TushkanMap,
                MarathonMap,
            };

            var pool = SProtoMan.Index(VanGuardPool);
            Assert.That(pool.Maps, Is.EquivalentTo(expected), "VanGuardMapPool must contain exactly the new rotation.");

            var packed = SProtoMan.Index(PackedMap);
            Assert.Multiple(() =>
            {
                Assert.That(packed.MinPlayers, Is.EqualTo(0u));
                Assert.That(packed.MaxPlayers, Is.EqualTo(uint.MaxValue));
            });

            var silly = SProtoMan.Index(SillyMap);
            Assert.Multiple(() =>
            {
                Assert.That(silly.MinPlayers, Is.EqualTo(0u));
                Assert.That(silly.MaxPlayers, Is.EqualTo(uint.MaxValue));
            });

            var saltern = SProtoMan.Index(SalternMap);
            Assert.That(saltern.MinPlayers, Is.EqualTo(8u));

            var cluster = SProtoMan.Index(ClusterMap);
            Assert.That(cluster.MinPlayers, Is.EqualTo(7u));

            var tushkan = SProtoMan.Index(TushkanMap);
            Assert.That(tushkan.MinPlayers, Is.EqualTo(5u));

            var marathon = SProtoMan.Index(MarathonMap);
            Assert.Multiple(() =>
            {
                Assert.That(marathon.MinPlayers, Is.EqualTo(35u));
                Assert.That(marathon.MaxPlayers, Is.EqualTo(uint.MaxValue));
            });

            var ticker = Server.System<ServerGameTicker>();
            Assert.Multiple(() =>
            {
                Assert.That(ticker.GetMinimumPlayerCount(TraitorPreset), Is.EqualTo(8));
                Assert.That(ticker.GetMinimumPlayerCount(WizardPreset), Is.EqualTo(10));
                Assert.That(ticker.GetMinimumPlayerCount(RevolutionaryPreset), Is.EqualTo(15));
                Assert.That(ticker.GetMinimumPlayerCount(NukeopsPreset), Is.EqualTo(20));
                Assert.That(ticker.GetMinimumPlayerCount(ZombiePreset), Is.EqualTo(20));
            });
        });
    }
}
