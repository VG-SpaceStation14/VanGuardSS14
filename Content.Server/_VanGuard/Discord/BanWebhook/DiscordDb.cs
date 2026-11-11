using System;
using Content.Shared.CCVar;
using Npgsql;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Content.Server._VanGuard.Discord;

public sealed partial class DiscordDb
{
    [Dependency] private IConfigurationManager _cfg = default!;
    private ISawmill _log = default!;

    public NpgsqlDataSource? DataSource { get; private set; }

    public void Initialize()
    {
        _log = Logger.GetSawmill("vg_discord_db");

        var pgHost = _cfg.GetCVar(CCVars.DatabasePgHost);
        var pgPort = _cfg.GetCVar(CCVars.DatabasePgPort);
        var pgDb = _cfg.GetCVar(CCVars.DatabasePgDatabase);
        var pgUser = _cfg.GetCVar(CCVars.DatabasePgUsername);
        var pgPass = _cfg.GetCVar(CCVars.DatabasePgPassword);

        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = pgHost,
            Port = pgPort,
            Database = pgDb,
            Username = pgUser,
            Password = pgPass,
            ApplicationName = "ss14-discord-auth",
            MaxPoolSize = 10,
        };

        DataSource = NpgsqlDataSource.Create(csb.ConnectionString);
        _log.Info("Discord DB initialized.");
    }
}
