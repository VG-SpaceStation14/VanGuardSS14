using Content.Shared.Directions;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Random;

namespace Content.Shared.Abilities.Goliath;

public sealed partial class GoliathTentacleSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<GoliathSummonTentacleAction>(OnSummonAction);
    }

    private void OnSummonAction(GoliathSummonTentacleAction args)
    {
        if (args.Handled)
            return;

        // TODO: animation

        _popup.PopupEntity(Loc.GetString("tentacle-ability-use-popup", ("entity", args.Performer)), args.Performer, type: PopupType.SmallCaution);
        _stun.TryAddStunDuration(args.Performer, TimeSpan.FromSeconds(0.8f));

        var coords = args.Target;
        List<EntityCoordinates> spawnPos = new();
        spawnPos.Add(coords);

        var dirs = new List<Direction>();
        dirs.AddRange(args.OffsetDirections);

        // VG-Tweak Start
        // Allow actions to choose the number of extra tentacles: the ancient
        // goliath uses two (three total) while the common goliath keeps three.
        for (var i = 0; i < args.ExtraSpawns; i++)
        {
            // VG-Tweak: ancient goliaths can request more than the four
            // cardinal directions.
            var dir = dirs.Count > 0
                ? _random.PickAndTake(dirs)
                : _random.Pick(new[]
                {
                    Direction.North, Direction.South, Direction.East, Direction.West,
                    Direction.NorthEast, Direction.SouthEast, Direction.SouthWest, Direction.NorthWest,
                });
            spawnPos.Add(coords.Offset(dir));
        }
        // VG-Tweak End

        if (_transform.GetGrid(coords) is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        foreach (var pos in spawnPos)
        {
            if (!_map.TryGetTileRef(grid, gridComp, pos, out var tileRef) ||
                _turf.IsSpace(tileRef) ||
                _turf.IsTileBlocked(tileRef, CollisionGroup.Impassable))
            {
                continue;
            }

            if (_net.IsServer)
                Spawn(args.EntityId, pos);
        }

        args.Handled = true;
    }
}
