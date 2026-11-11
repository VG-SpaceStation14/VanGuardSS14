#nullable enable
using Content.Server._VanGuard.Sponsors;
using NUnit.Framework;

namespace Content.IntegrationTests.Tests._VanGuard.Sponsors;

[TestFixture]
public sealed class SponsorPrivilegesTest
{
    [Test]
    public void TierPrivilegesMappingIsCorrect()
    {
        Assert.Multiple(() =>
        {
            // Tier 3
            Assert.That(DiscordSponsorManager.GetExtraSlotsForTier(3), Is.EqualTo(10), "Tier 3 must grant 10 extra slots.");
            Assert.That(DiscordSponsorManager.HasPriorityJoinForTier(3), Is.True, "Tier 3 must have priority join.");

            // Tier 2
            Assert.That(DiscordSponsorManager.GetExtraSlotsForTier(2), Is.EqualTo(5), "Tier 2 must grant 5 extra slots.");
            Assert.That(DiscordSponsorManager.HasPriorityJoinForTier(2), Is.True, "Tier 2 must have priority join.");

            // Tier 1
            Assert.That(DiscordSponsorManager.GetExtraSlotsForTier(1), Is.EqualTo(2), "Tier 1 must grant 2 extra slots.");
            Assert.That(DiscordSponsorManager.HasPriorityJoinForTier(1), Is.False, "Tier 1 must not have priority join.");

            // Tier 0
            Assert.That(DiscordSponsorManager.GetExtraSlotsForTier(0), Is.EqualTo(0), "Non-sponsors must not get extra slots.");
            Assert.That(DiscordSponsorManager.HasPriorityJoinForTier(0), Is.False, "Non-sponsors must not have priority join.");
        });
    }
}
