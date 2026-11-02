using System.Linq;
using Content.Server._VanGuard.Shuttles.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._VanGuard.Shuttles;
using Content.Shared.Access.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Shuttles.Systems;

/// <summary>
/// Server half of the simplified mining shuttle console. It only exposes tagged
/// docks as destinations and delegates the actual move to the existing shuttle
/// docking/FTL systems.
/// </summary>
public sealed partial class MiningShuttleConsoleSystem : EntitySystem
{
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MiningShuttleConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        Subs.BuiEvents<MiningShuttleConsoleComponent>(MiningShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<MiningShuttleConsoleTravelMessage>(OnTravel);
        });
    }

    private void OnOpened(Entity<MiningShuttleConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not MiningShuttleConsoleUiKey.Key)
            return;

        UpdateUi(ent);
    }

    private void OnTravel(Entity<MiningShuttleConsoleComponent> ent, ref MiningShuttleConsoleTravelMessage args)
    {
        var dock = GetEntity(args.Dock);
        TryTravel(ent, dock, args.Actor, out var error);
        UpdateUi(ent, error);
    }

    /// <summary>
    /// Returns the shuttle controlled by this console. A linked shuttle takes
    /// precedence; otherwise the console controls its own shuttle grid.
    /// </summary>
    public bool TryGetShuttle(Entity<MiningShuttleConsoleComponent> console, out EntityUid shuttle)
    {
        shuttle = default;

        if (console.Comp.LinkedShuttle is { } linked && Exists(linked))
        {
            shuttle = linked;
            return true;
        }

        if (Transform(console.Owner).GridUid is { } grid && HasComp<ShuttleComponent>(grid))
        {
            shuttle = grid;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the current dock list. This is public so the round-start/dock tests
    /// can inspect exactly what the UI would show without opening a client window.
    /// </summary>
    public List<MiningShuttleDockTarget> GetDockTargets(Entity<MiningShuttleConsoleComponent> console)
    {
        var result = new List<MiningShuttleDockTarget>();
        if (!TryGetShuttle(console, out var shuttle) ||
            !TryComp<ShuttleComponent>(shuttle, out _))
        {
            return result;
        }

        var query = AllEntityQuery<PriorityDockComponent, DockingComponent, TransformComponent>();
        while (query.MoveNext(out var dock, out var priority, out var docking, out var xform))
        {
            if (priority.Tag != console.Comp.DockTag)
                continue;

            if (xform.GridUid is not { } targetGrid || targetGrid == shuttle)
                continue;

            if (!Exists(targetGrid))
                continue;

            var targetMap = Transform(targetGrid).MapID;
            var name = GetDockName(dock, targetGrid);
            var config = _docking.GetDockingConfig(shuttle, targetGrid, console.Comp.DockTag);
            var enabled = config != null && !docking.Docked;
            var reason = (string?) null;

            if (docking.Docked)
                reason = Loc.GetString("mining-shuttle-console-dock-occupied");
            else if (!_shuttle.CanFTLTo(shuttle, targetMap, console.Owner))
                reason = Loc.GetString("mining-shuttle-console-destination-unavailable");
            else if (config == null)
                reason = Loc.GetString("mining-shuttle-console-no-dock-space");

            result.Add(new MiningShuttleDockTarget(dock, targetGrid, name, enabled, reason));
        }

        return result
            .OrderBy(target => target.Name)
            .ToList();
    }

    /// <summary>
    /// Starts travel to a tagged dock. Returns false (and a localized reason) for
    /// an inaccessible console, a missing/incorrect dock, a busy dock, FTL
    /// cooldown/travel state, or a missing shuttle.
    /// </summary>
    public bool TryTravel(
        Entity<MiningShuttleConsoleComponent> console,
        EntityUid dock,
        EntityUid actor,
        out string? error)
    {
        error = null;

        if (!Exists(dock))
        {
            error = Loc.GetString("mining-shuttle-console-invalid-dock");
            return false;
        }

        if (!_access.IsAllowed(actor, console.Owner))
        {
            error = Loc.GetString("mining-shuttle-console-access-denied");
            return false;
        }

        if (!TryGetShuttle(console, out var shuttle) ||
            !TryComp<ShuttleComponent>(shuttle, out var shuttleComp))
        {
            error = Loc.GetString("mining-shuttle-console-no-shuttle");
            return false;
        }

        if (!TryComp<PriorityDockComponent>(dock, out var priority) ||
            priority.Tag != console.Comp.DockTag)
        {
            error = Loc.GetString("mining-shuttle-console-invalid-dock");
            return false;
        }

        if (Transform(dock).GridUid is not { } targetGrid || targetGrid == shuttle)
        {
            error = Loc.GetString("mining-shuttle-console-invalid-dock");
            return false;
        }

        if (!_shuttle.CanFTL(shuttle, out var ftlReason))
        {
            error = ftlReason;
            return false;
        }

        var targetMap = Transform(targetGrid).MapID;
        if (!_shuttle.CanFTLTo(shuttle, targetMap, console.Owner))
        {
            error = Loc.GetString("mining-shuttle-console-destination-unavailable");
            return false;
        }

        if (_docking.GetDockingConfig(shuttle, targetGrid, console.Comp.DockTag) == null)
        {
            error = Loc.GetString("mining-shuttle-console-dock-occupied");
            return false;
        }

        if (!_shuttle.TryFTLDock(shuttle, shuttleComp, targetGrid, console.Comp.DockTag))
        {
            error = Loc.GetString("mining-shuttle-console-travel-failed");
            return false;
        }

        return true;
    }

    private void UpdateUi(Entity<MiningShuttleConsoleComponent> console, string? error = null)
    {
        List<MiningShuttleDockEntry> entries;

        if (!TryGetShuttle(console, out _))
        {
            entries = new List<MiningShuttleDockEntry>();
            error ??= Loc.GetString("mining-shuttle-console-no-shuttle");
        }
        else
        {
            entries = GetDockTargets(console)
                .Select(target => new MiningShuttleDockEntry(
                    GetNetEntity(target.Dock),
                    target.Name,
                    target.Enabled,
                    target.Reason))
                .ToList();
        }

        _ui.SetUiState(
            console.Owner,
            MiningShuttleConsoleUiKey.Key,
            new MiningShuttleConsoleState(entries, error));
    }

    private string GetDockName(EntityUid dock, EntityUid grid)
    {
        if (TryComp<DockLabelComponent>(dock, out var dockLabel) &&
            dockLabel.Label is { } dockLabelId)
        {
            return Loc.GetString(dockLabelId);
        }

        if (TryComp<DockLabelComponent>(grid, out var gridLabel) &&
            gridLabel.Label is { } gridLabelId)
        {
            return Loc.GetString(gridLabelId);
        }

        if (TryComp<MetaDataComponent>(grid, out var gridMeta) &&
            !string.IsNullOrWhiteSpace(gridMeta.EntityName) &&
            gridMeta.EntityName != "grid")
        {
            return gridMeta.EntityName;
        }

        if (TryComp<MetaDataComponent>(dock, out var dockMeta) &&
            !string.IsNullOrWhiteSpace(dockMeta.EntityName))
        {
            return dockMeta.EntityName;
        }

        return Loc.GetString("mining-shuttle-console-unknown-dock");
    }
}

/// <summary>
/// A target returned by <see cref="MiningShuttleConsoleSystem.GetDockTargets"/>.
/// </summary>
public readonly record struct MiningShuttleDockTarget(
    EntityUid Dock,
    EntityUid Grid,
    string Name,
    bool Enabled,
    string? Reason);
