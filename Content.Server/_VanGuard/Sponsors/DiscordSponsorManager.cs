using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Corvax.Interfaces.Shared;
using Content.Server._VanGuard.Discord;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Humanoid.Markings;
using Npgsql;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server._VanGuard.Sponsors;

public sealed partial class DiscordSponsorManager : ISharedSponsorsManager
{
    [Dependency] private DiscordDb _db = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IPrototypeManager _protoManager = default!;

    private ConcurrentDictionary<NetUserId, (int Tier, DateTime Until)> _cache = new();

    public void Initialize()
    {
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;

        Timer.SpawnRepeating(TimeSpan.FromMinutes(2), () =>
        {
            foreach (var session in _playerManager.Sessions)
                _ = RefreshAsync(session.UserId);
        }, CancellationToken.None);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Connected)
        {
            _ = RefreshAsync(e.Session.UserId);
        }
    }

    public async Task RefreshAsync(NetUserId userId)
    {
        if (_db.DataSource == null) return;

        try
        {
            await using var conn = await _db.DataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("""
                SELECT sponsor_tier, sponsor_until
                FROM discord_link_requests
                WHERE user_id = @uid AND status = 'linked';
                """, conn);
            cmd.Parameters.AddWithValue("uid", userId.UserId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var tier = reader.GetInt16(0);
                var until = reader.IsDBNull(1) ? DateTime.MinValue : reader.GetDateTime(1);
                _cache[userId] = (tier, until);
            }
            else
            {
                _cache.Remove(userId, out _);
            }
        }
        catch { /* Ignore database errors */ }
    }

    private int CurrentTier(NetUserId userId)
    {
        if (!_cache.TryGetValue(userId, out var entry)) return 0;
        return IsSubscriptionActive(entry.Until) ? entry.Tier : 0;
    }

    public int GetServerTier(NetUserId userId) => CurrentTier(userId);

    public bool TryGetServerPrototypes(NetUserId userId, [NotNullWhen(true)] out List<string>? prototypes)
    {
        var tier = CurrentTier(userId);
        if (tier <= 0)
        {
            prototypes = null;
            return false;
        }

        prototypes = GetSponsorPrototypes(tier);
        return true;
    }

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

            foreach (var marking in _protoManager.EnumeratePrototypes<MarkingPrototype>())
            {
                if (marking.SponsorOnly)
                    result.Add(marking.ID);
            }
        }

        return result;
    }

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

        if (_protoManager.TryIndex<MarkingPrototype>(prototypeId, out var marking) && marking.SponsorOnly)
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

    public bool TryGetServerOocColor(NetUserId userId, [NotNullWhen(true)] out Color? color)
    {
        var tier = CurrentTier(userId);
        if (tier <= 0) { color = null; return false; }

        color = tier switch
        {
            3 => Color.FromHex("#00ffaa"),
            2 => Color.FromHex("#00a6b2"),
            _ => Color.FromHex("#cc0000")
        };
        return true;
    }

    public int GetServerExtraCharSlots(NetUserId userId)
    {
        return GetExtraSlotsForTier(CurrentTier(userId));
    }

    public bool HaveServerPriorityJoin(NetUserId userId)
    {
        return HasPriorityJoinForTier(CurrentTier(userId));
    }

    public static bool IsSubscriptionActive(DateTime until) => until > DateTime.UtcNow;

    public static int GetExtraSlotsForTier(int tier) => tier switch
    {
        3 => 10,
        2 => 5,
        1 => 2,
        _ => 0
    };

    public static bool HasPriorityJoinForTier(int tier) => tier >= 2;
}
