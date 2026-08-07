using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Players.PlayTimeTracking;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Network;

namespace Content.Server._VanGuard.Administration.Commands;

/// <summary>
///     Shared plumbing for the playtime administration commands.
/// </summary>
internal static class PlayTimeCommandHelpers
{
    /// <summary>
    ///     Resolves a username or GUID to a player id, printing an error on failure.
    /// </summary>
    public static async Task<NetUserId?> ResolvePlayerAsync(
        IPlayerLocator locator,
        string input,
        IConsoleShell shell)
    {
        if (Guid.TryParse(input, out var guid))
            return new NetUserId(guid);

        var located = await locator.LookupIdByNameAsync(input);
        if (located == null)
        {
            shell.WriteError(Loc.GetString("parse-session-fail", ("username", input)));
            return null;
        }

        return located.UserId;
    }

    public static bool TryGetAmount(string raw, IConsoleShell shell, out uint minutes)
    {
        minutes = 0;
        if (uint.TryParse(raw, out minutes))
            return true;

        shell.WriteError(Loc.GetString("parse-minutes-fail", ("minutes", raw)));
        return false;
    }

    /// <summary>
    ///     Reads the overall playtime for a player, regardless of whether they are connected.
    /// </summary>
    public static async Task<TimeSpan> CurrentOverallAsync(
        IPlayerManager players,
        PlayTimeTrackingManager timers,
        NetUserId userId)
    {
        if (players.TryGetSessionById(userId, out var session))
            return timers.GetOverallPlaytime(session);

        return await timers.ReadOverallTimeAsync(userId);
    }
}
