#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Corvax.Interfaces.Shared;
using Content.Server._VanGuard.Sponsors;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Content.IntegrationTests.Fixtures;
using NUnit.Framework;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Sponsors;

[TestFixture]
[TestOf(typeof(SponsorLoadoutEffect))]
public sealed class SponsorLoadoutEffectTest : GameTest
{
    [Test]
    public async Task SponsorEffectBlocksAndAllowsBasedOnTier()
    {
        await Server.WaitAssertion(() =>
        {
            // Only look at loadouts that actually carry a SponsorLoadoutEffect.
            var sponsorLoadouts = SProtoMan.EnumeratePrototypes<LoadoutPrototype>()
                .Where(l => l.Effects.OfType<SponsorLoadoutEffect>().Any())
                .ToList();

            if (sponsorLoadouts.Count == 0)
            {
                // No sponsor loadouts exist yet. Skip, but say exactly why.
                // Once the first sponsor loadout is added, this test will run automatically.
                Assert.Ignore(
                    "No loadout in this build uses SponsorLoadoutEffect. " +
                    "Add the effect to a loadout prototype to enable this test.");
                return;
            }

            // Prefer the specific loadout if it exists AND has the effect; otherwise take any sponsor loadout.
            var proto = sponsorLoadouts.FirstOrDefault(l => l.ID == "ClothingNeckCloakCapFormalAltLoadout")
                ?? sponsorLoadouts[0];

            TestContext.Out.WriteLine($"[Diagnostic] Using loadout: {proto.ID}");
            TestContext.Out.WriteLine($"[Diagnostic] Total sponsor loadouts: {sponsorLoadouts.Count}");

            var effect = proto.Effects.OfType<SponsorLoadoutEffect>().First();
            var requiredTier = effect.Tier;
            TestContext.Out.WriteLine($"[Diagnostic] SponsorLoadoutEffect.Tier = {requiredTier}");

            // Expected reason comes from the same localization key the production code uses.
            // This keeps the test locale-agnostic (works for ru-RU, en-US, etc.).
            var expectedTierName = Loc.GetString($"discord-tier-{requiredTier}");
            TestContext.Out.WriteLine($"[Diagnostic] Expected tier name (current locale): {expectedTierName}");

            // Use the real production DiscordSponsorManager.
            var manager = IoCManager.Resolve<ISharedSponsorsManager>();
            Assert.That(manager, Is.InstanceOf<DiscordSponsorManager>(),
                "Production IoC should contain the DiscordSponsorManager.");

            var realManager = (DiscordSponsorManager)manager;
            var cacheField = typeof(DiscordSponsorManager).GetField("_cache",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(cacheField, Is.Not.Null, "DiscordSponsorManager._cache field should exist.");

            var cache = (ConcurrentDictionary<NetUserId, (int Tier, DateTime Until)>)
                cacheField!.GetValue(realManager)!;

            var collection = IoCManager.Instance!;
            var profile = HumanoidCharacterProfile.DefaultWithSpecies();
            var loadout = new RoleLoadout("TestRole");
            var userId = ServerSession!.UserId;

            try
            {
                // 1. Tier 0 — must be blocked with a tier-name reason.
                cache[userId] = (0, DateTime.UtcNow.AddHours(1));
                var result = effect.Validate(profile, loadout, proto, ServerSession, collection, out var reason);
                Assert.That(result, Is.False, "A player without a sponsor tier should not pass validation.");
                Assert.That(reason, Is.Not.Null, "A rejection reason must be provided for the UI.");
                Assert.That(reason!.ToString(), Does.Contain(expectedTierName),
                    $"The reason must contain the required tier name ('{expectedTierName}').");

                // 2. Tier (requiredTier - 1) — must be blocked if requiredTier > 1.
                if (requiredTier > 1)
                {
                    cache[userId] = (requiredTier - 1, DateTime.UtcNow.AddHours(1));
                    result = effect.Validate(profile, loadout, proto, ServerSession, collection, out reason);
                    Assert.That(result, Is.False,
                        $"A player with Tier {requiredTier - 1} should not pass a Tier {requiredTier} validation.");
                }

                // 3. Tier (requiredTier) — must be allowed.
                cache[userId] = (requiredTier, DateTime.UtcNow.AddHours(1));
                result = effect.Validate(profile, loadout, proto, ServerSession, collection, out reason);
                Assert.That(result, Is.True,
                    $"A player with Tier {requiredTier} should successfully pass validation.");
                Assert.That(reason, Is.Null, "No rejection reason should be provided when validation passes.");
            }
            finally
            {
                cache.TryRemove(userId, out _);
            }
        });
    }
}
