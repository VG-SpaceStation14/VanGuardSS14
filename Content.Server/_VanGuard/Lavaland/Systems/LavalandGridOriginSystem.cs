using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
namespace Content.Server._VanGuard.Lavaland.Systems;

/// <summary>
/// TEMPORARY diagnostic system. Records the life cycle of every map grid that
/// appears on the Lavaland planet: when it is created, what its prototype is,
/// where it is placed, and how its transform changes afterwards.
///
/// This exists only to trace where an unexpected grid comes from. It must be
/// removed once the investigation is finished.
/// </summary>
public sealed partial class LavalandGridOriginSystem : EntitySystem
{
    /// <summary>
    /// Enables recording. Disabled by default so the system is inert outside of
    /// the diagnostic test.
    /// </summary>
    public static bool Enabled;

    /// <summary>
    /// Recorded lines, in chronological order.
    /// </summary>
    public static readonly List<string> Log = new();

    /// <summary>
    /// The map that hosts the Lavaland planet.
    /// </summary>
    public static EntityUid PlanetMap = EntityUid.Invalid;

    /// <summary>
    /// The grid that carries the outpost.
    /// </summary>
    public static EntityUid OutpostGrid = EntityUid.Invalid;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MapGridComponent, MapInitEvent>(OnGridInit);
        SubscribeLocalEvent<EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);

        // MoveEvent is broadcast globally — there is no component-type for it
        // on the entity, so we hook the global broadcast handler instead.
        EntityManager.EventBus.SubscribeEvent<MoveEvent>(EventSource.Local, this, OnGlobalMove);

        var entMan = (EntityManager)IoCManager.Resolve<IEntityManager>();
        entMan.EntityQueueDeleted += OnEntityQueueDeleted;
    }

    private void OnEntityQueueDeleted(EntityUid uid)
    {
        if (!Enabled)
            return;

        if (!TryComp<MapGridComponent>(uid, out var gridComp))
            return;

        var xform = Transform(uid);
        var meta = Comp<MetaDataComponent>(uid);

        string physicsState = "none";
        if (TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(uid, out var p))
        {
            physicsState = $"bodyType={p.BodyType} canCollide={p.CanCollide}";
        }

        Record($"[grid-queued-delete] uid={uid.Id} " +
               $"proto={meta.EntityPrototype?.ID ?? "?"} " +
               $"parent={xform.ParentUid.Id} " +
               $"local=({xform.LocalPosition.X:F4},{xform.LocalPosition.Y:F4}) " +
               $"world=({xform.WorldPosition.X:F4},{xform.WorldPosition.Y:F4}) " +
               $"aabb=({gridComp.LocalAABB.Left:F2},{gridComp.LocalAABB.Bottom:F2})-({gridComp.LocalAABB.Right:F2},{gridComp.LocalAABB.Top:F2}) " +
               $"physics={physicsState}");
    }

    private void OnGridInit(EntityUid uid, MapGridComponent component, MapInitEvent args)
    {
        if (!Enabled)
            return;

        var meta = Comp<MetaDataComponent>(uid);
        var xform = Transform(uid);

        string physicsState = "none";
        if (TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(uid, out var physics))
        {
            physicsState = $"bodyType={physics.BodyType} canCollide={physics.CanCollide}";
        }

        Record($"[grid-init] uid={uid.Id} " +
               $"proto={meta.EntityPrototype?.ID ?? "?"} " +
               $"parent={(xform.ParentUid is { } p ? p.Id.ToString() : "null")} " +
               $"local=({xform.LocalPosition.X:F4},{xform.LocalPosition.Y:F4}) " +
               $"aabb=({component.LocalAABB.Left:F2},{component.LocalAABB.Bottom:F2})-({component.LocalAABB.Right:F2},{component.LocalAABB.Top:F2}) " +
               $"physics={physicsState}");
    }

    private void OnAnchorChanged(ref AnchorStateChangedEvent args)
    {
        if (!Enabled)
            return;

        if (!IsTrackedEntity(args.Entity))
            return;

        Record($"[anchor] uid={args.Entity.Id} anchored={args.Anchored} detaching={args.Detaching} " +
               $"world=({args.Transform.WorldPosition.X:F4},{args.Transform.WorldPosition.Y:F4})");
    }

    private void OnGlobalMove(ref MoveEvent ev)
    {
        if (!Enabled)
            return;

        if (!IsTrackedEntity(ev.Sender))
            return;

        // Skip events caused by parent changes — those are recorded by
        // [entity-parent] instead and would only inflate the log.
        if (ev.ParentChanged)
            return;

        // Skip pure rotations — they cannot be a symptom of the bug.
        if (ev.OnlyRotation)
            return;

        var gridBefore = ev.OldPosition.EntityId;
        var gridAfter = ev.NewPosition.EntityId;

        Record($"[move] uid={ev.Sender.Id} proto={ev.Entity.Comp2.EntityPrototype?.ID ?? "?"} " +
               $"gridBefore={gridBefore.Id} gridAfter={gridAfter.Id} " +
               $"oldLocal=({ev.OldPosition.Position.X:F4},{ev.OldPosition.Position.Y:F4}) " +
               $"newLocal=({ev.NewPosition.Position.X:F4},{ev.NewPosition.Position.Y:F4}) " +
               $"oldWorld=({ev.Component.WorldPosition.X:F4},{ev.Component.WorldPosition.Y:F4})");
    }

    /// <summary>
    /// The only outpost item prototypes this bug is about: the O2 locker
    /// (ClosetEmergencyFilledRandom), the pickaxe (Pickaxe) and the miner's
    /// locker (LockerMinerFilled). Re-parenting of any other entity is a
    /// normal part of loading the outpost and is deliberately not tracked.
    /// </summary>
    public static readonly HashSet<string> TrackedPrototypes = new()
    {
        "ClosetEmergencyFilledRandom",
        "Pickaxe",
        "LockerMinerFilled",
    };

    private bool IsTrackedEntity(EntityUid uid)
    {
        if (!TryComp<MetaDataComponent>(uid, out var meta))
            return false;

        var proto = meta.EntityPrototype?.ID;
        return proto is not null && TrackedPrototypes.Contains(proto);
    }

    /// <summary>
    /// The grid a tracked outpost item was born on. Only items that were born
    /// on <see cref="OutpostGrid"/> are relevant for this bug — a pickaxe that
    /// legitimately belongs to a mineshaft is not an outpost item.
    /// </summary>
    public static readonly Dictionary<EntityUid, EntityUid> TrackedOriginGrid = new();

    /// <summary>
    /// The grid a tracked outpost item is currently on, updated on every
    /// re-parent.
    /// </summary>
    public static readonly Dictionary<EntityUid, EntityUid> TrackedCurrentGrid = new();

    /// <summary>
    /// Tracked items that were deleted while the diagnostic was running.
    /// An outpost item that vanishes together with a rejected dungeon grid is
    /// the actual symptom of this bug, so deletion has to be tracked
    /// separately from re-parenting.
    /// </summary>
    public static readonly Dictionary<EntityUid, string> TrackedDeleted = new();

    /// <summary>
    /// Called every tick while the diagnostic is enabled so that the origin
    /// grid of every tracked item can be captured before any re-parenting
    /// happens.
    /// </summary>
    public static void CaptureTrackedOrigins()
    {
        if (!Enabled)
            return;

        var entMan = IoCManager.Resolve<IEntityManager>();

        var query = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            var proto = meta.EntityPrototype?.ID;
            if (proto is null || !TrackedPrototypes.Contains(proto))
                continue;

            var grid = xform.GridUid ?? EntityUid.Invalid;

            // Only capture the origin once. The origin is the grid the item was
            // born on and must never be overwritten by a later re-parent.
            if (!TrackedOriginGrid.ContainsKey(uid))
                TrackedOriginGrid[uid] = grid;

            TrackedCurrentGrid[uid] = grid;
        }
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent args)
    {
        if (!IsTrackedEntity(args.Entity))
            return;

        var meta = Comp<MetaDataComponent>(args.Entity);
        var grid = Transform(args.Entity).GridUid;

        // A tracked outpost item that is terminating is being deleted. When
        // that happens to an outpost item it vanished together with the grid
        // it was dragged onto — the actual symptom of this bug.
        TrackedDeleted[args.Entity] = meta.EntityPrototype?.ID ?? "?";
        Record($"[tracked-deleted] uid={args.Entity} " +
               $"proto={meta.EntityPrototype?.ID ?? "?"} " +
               $"grid={grid?.Id.ToString() ?? "null"}");
    }

    private void OnParentChanged(ref EntParentChangedMessage args)
    {
        if (!Enabled)
            return;

        // Outpost contents that get dragged onto a foreign grid are the symptom
        // we are looking for, so record them separately.
        if (!TryComp<MapGridComponent>(args.Entity, out var _))
        {
            var xform0 = args.Transform;
            var meta0 = Comp<MetaDataComponent>(args.Entity);

            // Keep the current grid of every tracked item up to date so the
            // test can tell whether an outpost item ended up on a foreign grid.
            if (IsTrackedEntity(args.Entity))
            {
                TrackedCurrentGrid[args.Entity] = xform0.GridUid ?? EntityUid.Invalid;

                // A brand new entity is being attached to its parent. Record the
                // grid it was born on — that is the grid it must stay on. This
                // has to happen here because an item can be created and
                // deleted again within a single tick, which the per-tick
                // sampling would miss.
                if (args.OldParent == EntityUid.Invalid &&
                    !TrackedOriginGrid.ContainsKey(args.Entity))
                {
                    TrackedOriginGrid[args.Entity] = xform0.GridUid ?? EntityUid.Invalid;
                }
            }

            Record($"[entity-parent] uid={args.Entity.Id} " +
                   $"proto={meta0.EntityPrototype?.ID ?? "?"} " +
                   $"oldParent={(args.OldParent is { } op0 ? op0.Id.ToString() : "null")} " +
                   $"newParent={xform0.ParentUid.Id} " +
                   $"grid={xform0.GridUid?.Id.ToString() ?? "null"} " +
                   $"local=({xform0.LocalPosition.X:F4},{xform0.LocalPosition.Y:F4}) " +
                   $"world=({xform0.WorldPosition.X:F4},{xform0.WorldPosition.Y:F4})");
            return;
        }

        if (!TryComp<MapGridComponent>(args.Entity, out var gridComp))
            return;

        var xform = args.Transform;
        var meta = Comp<MetaDataComponent>(args.Entity);

        Record($"[grid-parent] uid={args.Entity.Id} " +
               $"proto={meta.EntityPrototype?.ID ?? "?"} " +
               $"oldParent={(args.OldParent is { } op ? op.Id.ToString() : "null")} " +
               $"newParent={xform.ParentUid.Id} " +
               $"local=({xform.LocalPosition.X:F4},{xform.LocalPosition.Y:F4}) " +
               $"world=({xform.WorldPosition.X:F4},{xform.WorldPosition.Y:F4}) " +
               $"grid={xform.GridUid?.Id.ToString() ?? "null"} " +
               $"aabb=({gridComp.LocalAABB.Left:F2},{gridComp.LocalAABB.Bottom:F2})-({gridComp.LocalAABB.Right:F2},{gridComp.LocalAABB.Top:F2})");
    }

    /// <summary>
    /// Called by the diagnostic test to snapshot the current state of all grids.
    /// </summary>
    public static void Snapshot(string label)
    {
        if (!Enabled)
            return;

        var entMan = IoCManager.Resolve<IEntityManager>();

        Record($"[snapshot {label}]");

        var query = entMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var gridComp, out var xform))
        {
            var parent = xform.ParentUid;
            var parentDesc = "null";
            if (entMan.TryGetComponent<MapComponent>(parent, out _))
                parentDesc = $"MAP({parent.Id})";
            else if (entMan.TryGetComponent<MapGridComponent>(parent, out _))
                parentDesc = $"GRID({parent.Id})";

            var world = xform.WorldPosition;
            var origin = world - xform.LocalPosition;

            var dist = "?";
            if (OutpostGrid != EntityUid.Invalid &&
                entMan.TryGetComponent<TransformComponent>(OutpostGrid, out var outpostXform))
            {
                dist = (origin - outpostXform.WorldPosition).Length().ToString("F3");
            }

            Record($"    grid uid={uid.Id} " +
                   $"proto={entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? "?"} " +
                   $"parent={parentDesc} " +
                   $"local=({xform.LocalPosition.X:F4},{xform.LocalPosition.Y:F4}) " +
                   $"world=({world.X:F4},{world.Y:F4}) " +
                   $"origin=({origin.X:F4},{origin.Y:F4}) " +
                   $"aabb=({gridComp.LocalAABB.Left:F2},{gridComp.LocalAABB.Bottom:F2})-({gridComp.LocalAABB.Right:F2},{gridComp.LocalAABB.Top:F2}) " +
                   $"distToOutpost={dist}");
        }
    }

    private static void Record(string line)
    {
        Log.Add(line);
    }

    /// <summary>
    /// Grids whose position is being sampled every tick, so a grid that is moved
    /// or collides can be followed step by step.
    /// </summary>
    public static readonly HashSet<EntityUid> SampledGrids = new();

    /// <summary>
    /// Records the position of all grids (when <see cref="SampleAll"/> is set)
    /// or only of the sampled ones.
    /// </summary>
    public static void SampleTick(int tick)
    {
        if (!Enabled)
            return;

        var entMan = IoCManager.Resolve<IEntityManager>();

        if (SampleAll)
        {
            var query = entMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var gridComp, out var xform))
            {
                EmitTickSample(tick, uid, gridComp, xform);
            }

            return;
        }

        if (SampledGrids.Count == 0)
            return;

        foreach (var uid in SampledGrids)
        {
            if (!entMan.TryGetComponent<TransformComponent>(uid, out var xform))
                continue;

            if (!entMan.TryGetComponent<MapGridComponent>(uid, out var gridComp))
                continue;

            EmitTickSample(tick, uid, gridComp, xform);
        }
    }

    private static void EmitTickSample(int tick, EntityUid uid, MapGridComponent gridComp, TransformComponent xform)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        var world = xform.WorldPosition;
        var origin = world - xform.LocalPosition;

        var dist = "?";
        if (OutpostGrid != EntityUid.Invalid &&
            entMan.TryGetComponent<TransformComponent>(OutpostGrid, out var outpostXform))
        {
            dist = (origin - outpostXform.WorldPosition).Length().ToString("F3");
        }

        Record($"[tick {tick}] grid uid={uid.Id} " +
               $"parent={xform.ParentUid.Id} " +
               $"local=({xform.LocalPosition.X:F4},{xform.LocalPosition.Y:F4}) " +
               $"world=({world.X:F4},{world.Y:F4}) " +
               $"origin=({origin.X:F4},{origin.Y:F4}) " +
               $"distToOutpost={dist}");
    }

    /// <summary>
    /// When true, every grid is sampled on every tick instead of just
    /// <see cref="SampledGrids"/>.
    /// </summary>
    public static bool SampleAll = true;

    public static void Clear()
    {
        Log.Clear();
        SampledGrids.Clear();
        TrackedOriginGrid.Clear();
        TrackedCurrentGrid.Clear();
        TrackedDeleted.Clear();
        PlanetMap = EntityUid.Invalid;
        OutpostGrid = EntityUid.Invalid;
    }
}
