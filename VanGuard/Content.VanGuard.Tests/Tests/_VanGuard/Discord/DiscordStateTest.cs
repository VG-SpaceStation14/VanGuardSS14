#nullable enable
using System;
using Content.Server._VanGuard.Sponsors;
using NUnit.Framework;

namespace Content.IntegrationTests.Tests._VanGuard.Discord;

[TestFixture]
public sealed class DiscordStateTest
{
    [Test]
    public void ExpiredSponsorIsTreatedAsInactive()
    {
        var untilExpired = DateTime.UtcNow.AddMinutes(-5);
        var untilActive = DateTime.UtcNow.AddMinutes(30);

        Assert.Multiple(() =>
        {
            Assert.That(DiscordSponsorManager.IsSubscriptionActive(untilExpired), Is.False, 
                "An expired subscription must be treated as inactive.");
            Assert.That(DiscordSponsorManager.IsSubscriptionActive(untilActive), Is.True, 
                "A valid subscription must be treated as active.");
        });
    }
}
