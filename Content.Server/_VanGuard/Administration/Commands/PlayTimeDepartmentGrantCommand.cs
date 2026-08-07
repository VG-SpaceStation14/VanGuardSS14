using System.Linq;
using Content.Server.Administration;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared.Administration;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Administration.Commands;

[AdminCommand(AdminFlags.Moderator)]
public sealed partial class GrantDepartmentPlaytimeCommand : IConsoleCommand
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private PlayTimeTrackingManager _timers = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public string Command => "playtime_adddepartment_as";
    public string Description => "Grants playtime to every role of a department, split evenly between them.";
    public string Help => $"Usage: {Command} <username/guid> <department> <minutes>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteError("Expected: <username/guid> <department> <minutes>");
            return;
        }

        var userId = await PlayTimeCommandHelpers.ResolvePlayerAsync(_locator, args[0], shell);
        if (userId == null)
            return;

        if (!PlayTimeCommandHelpers.TryGetAmount(args[2], shell, out var minutes))
            return;

        if (!_prototypes.TryIndex<DepartmentPrototype>(args[1], out var department))
        {
            shell.WriteError($"Unknown department: {args[1]}");
            return;
        }

        var roles = department.Roles
            .Select(id => _prototypes.TryIndex<JobPrototype>(id, out var job) ? job : null)
            .Where(job => job != null && !string.IsNullOrEmpty(job.PlayTimeTracker))
            .ToList();

        if (roles.Count == 0)
        {
            shell.WriteError($"Department '{args[1]}' has no trackable roles.");
            return;
        }

        var perRole = TimeSpan.FromMinutes(minutes / (double)roles.Count);
        foreach (var job in roles)
        {
            if (_players.TryGetSessionById(userId.Value, out var session))
                _timers.AddTimeToTracker(session, job!.PlayTimeTracker, perRole);
            else
                await _timers.GrantTrackerTimeAsync(userId.Value, job!.PlayTimeTracker, perRole);
        }

        var overall = await PlayTimeCommandHelpers.CurrentOverallAsync(_players, _timers, userId.Value);
        var names = string.Join(", ", roles.Select(j => j!.LocalizedName));

        shell.WriteLine($"Split {minutes} minutes across {roles.Count} roles of '{args[1]}' for {args[0]}. " +
                        $"Overall: {overall.TotalMinutes:F0}m. Roles: {names}");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), "Username or GUID");

        if (args.Length == 2)
        {
            var departments = _prototypes.EnumeratePrototypes<DepartmentPrototype>()
                .Select(d => d.ID)
                .OrderBy(id => id)
                .ToArray();

            return CompletionResult.FromHintOptions(departments, "Department ID");
        }

        if (args.Length == 3)
            return CompletionResult.FromHint("Minutes (total)");

        return CompletionResult.Empty;
    }
}
