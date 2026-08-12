using System.Linq;
using Content.Server.Administration;
using Content.Server._VanGuard.Language;
using Content.Shared._VanGuard.Language;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Language.Commands;

/// <summary>
///     Shared helpers for language admin commands.
/// </summary>
public static class LanguageCommandHelpers
{
    public static bool TryResolveTarget(string input, IConsoleShell shell, IPlayerManager players, IEntityManager entities, out EntityUid? target)
    {
        target = null;

        if (players.TryGetSessionByUsername(input, out var session) && session.AttachedEntity is { Valid: true } entity)
        {
            target = entity;
            return true;
        }

        if (EntityUid.TryParse(input, out var uid) && entities.EntityExists(uid))
        {
            target = uid;
            return true;
        }

        shell.WriteError(Loc.GetString("cmd-language-target-not-found", ("target", input)));
        return false;
    }

    public static CompletionResult PlayerCompletion(IPlayerManager players)
    {
        return CompletionResult.FromHintOptions(
            players.Sessions.Where(s => s.AttachedEntity is { Valid: true }).Select(s => s.Name),
            Loc.GetString("cmd-language-hint-player"));
    }
}

/// <summary>
///     Grants a language to a connected player.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class AddLanguageCommand : LocalizedCommands
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntityManager _entities = default!;

    public override string Command => "addlanguage";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("cmd-language-wrong-arguments", ("command", Command), ("expected", 2), ("got", args.Length)));
            return;
        }

        if (!LanguageCommandHelpers.TryResolveTarget(args[0], shell, _players, _entities, out var target))
            return;

        if (!_prototypes.HasIndex(new ProtoId<LanguagePrototype>(args[1])))
        {
            shell.WriteError(Loc.GetString("cmd-language-not-found", ("language", args[1])));
            return;
        }

        _entities.System<LanguageSystem>().GrantLanguage(target!.Value, args[1]);
        shell.WriteLine(Loc.GetString("cmd-addlanguage-success", ("target", target.Value), ("language", args[1])));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => LanguageCommandHelpers.PlayerCompletion(_players),
            2 => CompletionResult.FromHintOptions(
                _prototypes.EnumeratePrototypes<LanguagePrototype>().Select(p => p.ID),
                Loc.GetString("cmd-language-hint-language")),
            _ => CompletionResult.Empty,
        };
    }
}

/// <summary>
///     Removes a language from a connected player.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class RemoveLanguageCommand : LocalizedCommands
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntityManager _entities = default!;

    public override string Command => "removelanguage";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("cmd-language-wrong-arguments", ("command", Command), ("expected", 2), ("got", args.Length)));
            return;
        }

        if (!LanguageCommandHelpers.TryResolveTarget(args[0], shell, _players, _entities, out var target))
            return;

        if (!_prototypes.HasIndex(new ProtoId<LanguagePrototype>(args[1])))
        {
            shell.WriteError(Loc.GetString("cmd-language-not-found", ("language", args[1])));
            return;
        }

        _entities.System<LanguageSystem>().RevokeLanguage(target!.Value, args[1]);
        shell.WriteLine(Loc.GetString("cmd-removelanguage-success", ("target", target.Value), ("language", args[1])));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => LanguageCommandHelpers.PlayerCompletion(_players),
            2 => CompletionResult.FromHintOptions(
                _prototypes.EnumeratePrototypes<LanguagePrototype>().Select(p => p.ID),
                Loc.GetString("cmd-language-hint-language")),
            _ => CompletionResult.Empty,
        };
    }
}

/// <summary>
///     Lists every language known by a target player.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class ListLanguagesCommand : LocalizedCommands
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IEntityManager _entities = default!;

    public override string Command => "listlanguages";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("cmd-language-wrong-arguments", ("command", Command), ("expected", 1), ("got", args.Length)));
            return;
        }

        if (!_players.TryGetSessionByUsername(args[0], out var session) || session.AttachedEntity is not { Valid: true } entity)
        {
            shell.WriteError(Loc.GetString("cmd-language-target-not-found", ("target", args[0])));
            return;
        }

        var language = _entities.System<LanguageSystem>();
        if (!language.RetrieveKnownLanguages(entity, LanguageKnowledge.Understand, out var langs, out _))
        {
            shell.WriteLine(Loc.GetString("cmd-listlanguages-empty", ("target", args[0])));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-listlanguages-title", ("target", args[0])));
        foreach (var (id, knowledge) in langs.OrderBy(x => x.Key))
        {
            shell.WriteLine($"{id}: {knowledge}");
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => LanguageCommandHelpers.PlayerCompletion(_players),
            _ => CompletionResult.Empty,
        };
    }
}

