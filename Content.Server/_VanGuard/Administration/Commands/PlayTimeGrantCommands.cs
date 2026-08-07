using System.Linq;
using Content.Server.Administration;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared.Administration;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Administration.Commands;

[AdminCommand(AdminFlags.Moderator)]
public sealed partial class GrantRolePlaytimeCommand : IConsoleCommand
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private PlayTimeTrackingManager _timers = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public string Command => "playtime_addrole_as";
    public string Description => "Grants playtime on a specific role to a player.";
    public string Help => $"Usage: {Command} <username/guid> <role> <minutes>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteError("Expected: <username/guid> <role> <minutes>");
            return;
        }

        var userId = await PlayTimeCommandHelpers.ResolvePlayerAsync(_locator, args[0], shell);
        if (userId == null)
            return;

        if (!PlayTimeCommandHelpers.TryGetAmount(args[2], shell, out var minutes))
            return;

        var tracker = ResolveTracker(args[1]);
        if (tracker == null)
        {
            shell.WriteError($"Unknown role or tracker: {args[1]}");
            return;
        }

        var amount = TimeSpan.FromMinutes(minutes);
        if (_players.TryGetSessionById(userId.Value, out var session))
            _timers.AddTimeToTracker(session, tracker, amount);
        else
            await _timers.GrantTrackerTimeAsync(userId.Value, tracker, amount);

        var roleTime = _players.TryGetSessionById(userId.Value, out var online)
            ? _timers.GetPlayTimeForTracker(online, tracker)
            : await _timers.ReadTrackerTimeAsync(userId.Value, tracker);

        var overall = await PlayTimeCommandHelpers.CurrentOverallAsync(_players, _timers, userId.Value);

        shell.WriteLine($"Granted {minutes} minutes on '{args[1]}' to {args[0]}. " +
                        $"Role time: {roleTime.TotalMinutes:F0}m, overall: {overall.TotalMinutes:F0}m.");
    }

    private string? ResolveTracker(string input)
    {
        if (_prototypes.TryIndex<JobPrototype>(input, out var job) && !string.IsNullOrEmpty(job.PlayTimeTracker))
            return job.PlayTimeTracker;

        if (_prototypes.HasIndex<PlayTimeTrackerPrototype>(input))
            return input;

        return null;
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), "Username or GUID");

        if (args.Length == 2)
        {
            var roles = _prototypes.EnumeratePrototypes<JobPrototype>().Select(j => j.ID)
                .Concat(_prototypes.EnumeratePrototypes<PlayTimeTrackerPrototype>().Select(t => t.ID))
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            return CompletionResult.FromHintOptions(roles, "Role or tracker ID");
        }

        if (args.Length == 3)
            return CompletionResult.FromHint("Minutes");

        return CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Moderator)]
public sealed partial class GrantOverallPlaytimeCommand : IConsoleCommand
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private PlayTimeTrackingManager _timers = default!;
    [Dependency] private IPlayerLocator _locator = default!;

    public string Command => "playtime_addoverall_as";
    public string Description => "Grants playtime to a player's overall timer.";
    public string Help => $"Usage: {Command} <username/guid> <minutes>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError("Expected: <username/guid> <minutes>");
            return;
        }

        var userId = await PlayTimeCommandHelpers.ResolvePlayerAsync(_locator, args[0], shell);
        if (userId == null)
            return;

        if (!PlayTimeCommandHelpers.TryGetAmount(args[1], shell, out var minutes))
            return;

        var amount = TimeSpan.FromMinutes(minutes);
        TimeSpan overall;
        if (_players.TryGetSessionById(userId.Value, out var session))
        {
            _timers.AddTimeToOverallPlaytime(session, amount);
            overall = _timers.GetOverallPlaytime(session);
        }
        else
        {
            await _timers.GrantOverallTimeAsync(userId.Value, amount);
            overall = await _timers.ReadOverallTimeAsync(userId.Value);
        }

        shell.WriteLine($"Overall playtime for {args[0]} is now {overall.TotalMinutes:F0} minutes.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), "Username or GUID");

        if (args.Length == 2)
            return CompletionResult.FromHint("Minutes");

        return CompletionResult.Empty;
    }
}
