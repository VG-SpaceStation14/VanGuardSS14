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
using Npgsql;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
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
        catch { /* Игнорируем ошибки БД */ }
    }

    private int CurrentTier(NetUserId userId)
    {
        if (!_cache.TryGetValue(userId, out var entry)) return 0;
        return entry.Until > DateTime.UtcNow ? entry.Tier : 0;
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

        // Ищем лоадауты с эффектом SponsorLoadoutEffect
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

        // Ищем спонсорские расы
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

    public bool TryGetTierNameForPrototype(string prototypeId, [NotNullWhen(true)] out string? tierName)
    {
        if (_protoManager.TryIndex<LoadoutPrototype>(prototypeId, out var loadout))
        {
            foreach (var effect in loadout.Effects)
            {
                if (effect is SponsorLoadoutEffect sponsorEffect)
                {
                    tierName = sponsorEffect.Tier switch
                    {
                        3 => "Космический ниндзя",
                        2 => "Революционер",
                        1 => "Агент Синдиката",
                        _ => null
                    };
                    return tierName != null;
                }
            }
        }

        if (_protoManager.TryIndex<SpeciesPrototype>(prototypeId, out var species) && species.SponsorOnly)
        {
            tierName = "Космический ниндзя";
            return true;
        }

        tierName = null;
        return false;
    }

    public bool TryGetServerOocColor(NetUserId userId, [NotNullWhen(true)] out Color? color)
    {
        var tier = CurrentTier(userId);
        if (tier <= 0) { color = null; return false; }

        color = tier switch{
            3 => Color.FromHex("#FF0000"),
            2 => Color.FromHex("#FFA500"),
            _ => Color.FromHex("#FFFF00")
        };
        return true;
    }

    public int GetServerExtraCharSlots(NetUserId userId)
    {
        var tier = CurrentTier(userId);
        return tier switch { 3 => 5, 2 => 3, 1 => 1, _ => 0 };
    }

    public bool HaveServerPriorityJoin(NetUserId userId) => CurrentTier(userId) >= 2;
}
