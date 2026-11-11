using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Corvax.Interfaces.Shared;
using Content.Client._VanGuard.Discord;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Client._VanGuard.Sponsors;

public sealed partial class ClientSponsorManager : ISharedSponsorsManager
{
    [Dependency] private IPrototypeManager _protoManager = default!;
    [Dependency] private ClientDiscordAuthManager _auth = default!;

    public void Initialize() { }

    public int GetServerTier(NetUserId userId) => _auth.CurrentTier;

    public List<string> GetClientPrototypes()
    {
        return GetSponsorPrototypes(int.MaxValue);
    }

    private List<string> GetSponsorPrototypes(int maxTier)
    {
        var result = new List<string>();

        foreach (var loadout in _protoManager.EnumeratePrototypes<LoadoutPrototype>())
        {
            foreach (var effect in loadout.Effects)
            {
                if (effect is SponsorLoadoutEffect sponsorEffect && sponsorEffect.Tier <= maxTier)
                {
                    result.Add(loadout.ID);
                    break;
                }
            }
        }

        if (maxTier >= 1)
        {
            foreach (var species in _protoManager.EnumeratePrototypes<SpeciesPrototype>())
            {
                if (species.SponsorOnly)
                    result.Add(species.ID);
            }
        }

        return result;
    }

    public bool TryGetServerPrototypes(NetUserId userId, [NotNullWhen(true)] out List<string>? prototypes)
    {
        prototypes = null;
        return false;
    }

    public bool TryGetServerOocColor(NetUserId userId, [NotNullWhen(true)] out Color? color)
    {
        color = null;
        return false;
    }

    public int GetServerExtraCharSlots(NetUserId userId) => 0;

    public bool HaveServerPriorityJoin(NetUserId userId) => false;

    public bool TryGetTierNameForPrototype(string prototypeId, [NotNullWhen(true)] out string? tierName)
    {
        if (_protoManager.TryIndex<LoadoutPrototype>(prototypeId, out var loadout))
        {
            foreach (var effect in loadout.Effects)
            {
                if (effect is SponsorLoadoutEffect sponsorEffect)
                {
                    tierName = GetTierName(sponsorEffect.Tier);
                    return tierName != null;
                }
            }
        }

        if (_protoManager.TryIndex<SpeciesPrototype>(prototypeId, out var species) && species.SponsorOnly)
        {
            tierName = Loc.GetString("discord-tier-3");
            return true;
        }

        tierName = null;
        return false;
    }

    private string? GetTierName(int tier) => tier switch
    {
        3 => Loc.GetString("discord-tier-3"),
        2 => Loc.GetString("discord-tier-2"),
        1 => Loc.GetString("discord-tier-1"),
        _ => null
    };
}
