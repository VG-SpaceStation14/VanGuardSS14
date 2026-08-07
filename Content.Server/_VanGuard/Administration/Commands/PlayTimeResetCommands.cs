using System.Linq;
using Content.Server.Administration;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared.Administration;
using Content.Shared.Players.PlayTimeTracking;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Administration.Commands;

[AdminCommand(AdminFlags.Moderator)]
public sealed partial class ClearAllPlaytimeCommand : IConsoleCommand
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private PlayTimeTrackingManager _timers = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public string Command => "playtime_resetall_as";
    public string Description => "Clears all playtime (overall and every role) for a player.";
    public string Help => $"Usage: {Command} <username/guid>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError("Expected: <username/guid>");
            return;
        }

        var userId = await PlayTimeCommandHelpers.ResolvePlayerAsync(_locator, args[0], shell);
        if (userId == null)
            return;

        if (_players.TryGetSessionById(userId.Value, out var session))
        {
            _timers.ClearOverallTime(session);
            foreach (var tracker in _prototypes.EnumeratePrototypes<PlayTimeTrackerPrototype>())
                _timers.ClearTrackerTime(session, tracker.ID);
        }
        else
        {
            await _timers.ClearAllTimeAsync(userId.Value);
        }

        var overall = await PlayTimeCommandHelpers.CurrentOverallAsync(_players, _timers, userId.Value);
        shell.WriteLine($"All playtime for {args[0]} has been cleared. Overall is now {overall.TotalMinutes:F0}m.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), "Username or GUID");

        return CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Moderator)]
public sealed partial class ClearRolesPlaytimeCommand : IConsoleCommand
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private PlayTimeTrackingManager _timers = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public string Command => "playtime_resetroles_as";
    public string Description => "Clears every role timer for a player while leaving overall playtime intact.";
    public string Help => $"Usage: {Command} <username/guid>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError("Expected: <username/guid>");
            return;
        }

        var userId = await PlayTimeCommandHelpers.ResolvePlayerAsync(_locator, args[0], shell);
        if (userId == null)
            return;

        var overallBefore = await PlayTimeCommandHelpers.CurrentOverallAsync(_players, _timers, userId.Value);

        var trackers = _prototypes.EnumeratePrototypes<PlayTimeTrackerPrototype>()
            .Where(t => t.ID != PlayTimeTrackingShared.TrackerOverall)
            .ToList();

        if (_players.TryGetSessionById(userId.Value, out var session))
        {
            foreach (var tracker in trackers)
                _timers.ClearTrackerTime(session, tracker.ID);
        }
        else
        {
            foreach (var tracker in trackers)
                await _timers.ClearTrackerTimeAsync(userId.Value, tracker.ID);
        }

        var overallAfter = await PlayTimeCommandHelpers.CurrentOverallAsync(_players, _timers, userId.Value);
        shell.WriteLine($"Role timers for {args[0]} have been cleared. " +
                        $"Overall went from {overallBefore.TotalMinutes:F0}m to {overallAfter.TotalMinutes:F0}m.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), "Username or GUID");

        return CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Moderator)]
public sealed partial class ClearOverallPlaytimeCommand : IConsoleCommand
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private PlayTimeTrackingManager _timers = default!;
    [Dependency] private IPlayerLocator _locator = default!;

    public string Command => "playtime_resetoverall_as";
    public string Description => "Clears the overall playtime of a player while keeping role timers.";
    public string Help => $"Usage: {Command} <username/guid>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError("Expected: <username/guid>");
            return;
        }

        var userId = await PlayTimeCommandHelpers.ResolvePlayerAsync(_locator, args[0], shell);
        if (userId == null)
            return;

        var before = await PlayTimeCommandHelpers.CurrentOverallAsync(_players, _timers, userId.Value);

        if (_players.TryGetSessionById(userId.Value, out var session))
            _timers.ClearOverallTime(session);
        else
            await _timers.ClearOverallTimeAsync(userId.Value);

        var after = await PlayTimeCommandHelpers.CurrentOverallAsync(_players, _timers, userId.Value);
        shell.WriteLine($"Overall playtime for {args[0]} cleared: {before.TotalMinutes:F0}m -> {after.TotalMinutes:F0}m.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), "Username or GUID");

        return CompletionResult.Empty;
    }
}

