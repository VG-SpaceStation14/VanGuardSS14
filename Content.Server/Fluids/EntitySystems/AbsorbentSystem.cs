// VG-Tweak Start
using System.Numerics;
using Content.Shared._VanGuard.Footprints;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Interaction;
using Content.Shared.Timing.Components;
using Content.Shared.Timing.Systems;
using Content.Shared.Weapons.Melee;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
// VG-Tweak End
using Content.Shared.Fluids;

namespace Content.Server.Fluids.EntitySystems;

// VG-Tweak Start
/// <inheritdoc/>
public sealed partial class AbsorbentSystem : SharedAbsorbentSystem
{
    private const float MaxCleanRadius = 3f;

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    protected override void OnAfterInteract(Entity<AbsorbentComponent> ent, ref AfterInteractEvent args)
    {
        // Keep ordinary single-puddle mopping on the shared path. Area cleanup is
        // needed for footprints and for a puddle that has footprints around it.
        if (!args.Handled
            && args.CanReach
            && args.Target is { } puddleTarget
            && HasComp<PuddleComponent>(puddleTarget)
            && !HasFootprintNear(Transform(puddleTarget).Coordinates, ent.Comp.CleanRadius)
            && !HasOtherPuddleNear(Transform(puddleTarget).Coordinates, ent.Comp.CleanRadius, puddleTarget))
        {
            base.OnAfterInteract(ent, ref args);
            return;
        }

        if (!args.Handled && args.CanReach)
        {
            EntityCoordinates? center = null;
            if (args.Target is { } target)
            {
                if (HasComp<PuddleComponent>(target) || HasComp<FootprintComponent>(target))
                    center = Transform(target).Coordinates;
            }
            else
            {
                center = args.ClickLocation;
            }

            if (center is { } location && TryCleanArea(ent, args.User, location))
            {
                args.Handled = true;
                return;
            }
        }

        base.OnAfterInteract(ent, ref args);
    }

    private bool HasFootprintNear(EntityCoordinates center, float radius)
    {
        var safeRadius = Math.Clamp(radius, 0f, MaxCleanRadius);
        if (safeRadius <= 0f)
            return false;

        foreach (var uid in _lookup.GetEntitiesInRange(center, safeRadius))
        {
            if (HasComp<FootprintComponent>(uid))
                return true;
        }

        return false;
    }

    private bool HasOtherPuddleNear(EntityCoordinates center, float radius, EntityUid excluded)
    {
        var safeRadius = Math.Clamp(radius, 0f, MaxCleanRadius);
        if (safeRadius <= 0f)
            return false;

        foreach (var uid in _lookup.GetEntitiesInRange(center, safeRadius))
        {
            if (uid != excluded && HasComp<PuddleComponent>(uid))
                return true;
        }

        return false;
    }

    private bool TryCleanArea(Entity<AbsorbentComponent> ent, EntityUid user, EntityCoordinates center)
    {
        var radius = Math.Clamp(ent.Comp.CleanRadius, 0f, MaxCleanRadius);
        if (radius <= 0f)
            return false;

        if (TryComp<UseDelayComponent>(ent, out var useDelay)
            && _useDelay.IsDelayed((ent.Owner, useDelay)))
            return false;

        if (!SolutionContainer.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var absorberSoln))
            return false;

        var candidates = _lookup.GetEntitiesInRange(center, radius);
        var cleaned = 0;

        foreach (var uid in candidates)
        {
            if (!TryComp<PuddleComponent>(uid, out var puddle))
                continue;

            if (TryCleanPuddle(uid, puddle, absorberSoln.Value, ent))
                cleaned++;
        }

        if (cleaned == 0)
            return false;

        _audio.PlayPvs(ent.Comp.PickupSound, ent.Owner);

        if (useDelay != null)
            _useDelay.TryResetDelay((ent, useDelay));

        var userXform = Transform(user);
        var targetPos = _transform.ToMapCoordinates(center).Position;
        var localPos = Vector2.Transform(targetPos, _transform.GetInvWorldMatrix(userXform));
        localPos = userXform.LocalRotation.RotateVec(localPos);
        _melee.DoLunge(user, ent, Angle.Zero, localPos, null);

        return true;
    }

    private bool TryCleanPuddle(
        EntityUid uid,
        PuddleComponent puddle,
        Entity<SolutionComponent> absorberSoln,
        Entity<AbsorbentComponent> absorbent)
    {
        if (!SolutionContainer.ResolveSolution(uid, puddle.SolutionName, ref puddle.Solution, out var puddleSolution)
            || puddleSolution.Volume <= 0)
            return false;

        var absorberSolution = absorberSoln.Comp.Solution;

        if (!absorbent.Comp.UseAbsorberSolution)
        {
            var absorbed = FixedPoint2.Min(absorberSolution.AvailableVolume, absorbent.Comp.PickupAmount);
            if (absorbed <= 0)
                return false;

            var split = puddleSolution.SplitSolution(absorbed);
            SolutionContainer.AddSolution(absorberSoln, split);

            if (puddleSolution.Volume <= 0)
                QueueDel(uid);

            return split.Volume > 0;
        }

        var absorbentReagents = Puddle.GetAbsorbentReagents(puddleSolution);
        if (puddleSolution.GetTotalPrototypeQuantity(absorbentReagents) == puddleSolution.Volume)
            return false;

        var available = absorberSolution.GetTotalPrototypeQuantity(Puddle.GetAbsorbentReagents(absorberSolution));
        if (available <= 0)
            return false;

        var transfer = FixedPoint2.Min(available, absorbent.Comp.PickupAmount);
        var contaminants = puddleSolution.SplitSolutionWithout(transfer, absorbentReagents);
        if (contaminants.Volume <= 0)
            return false;

        SolutionContainer.AddSolution(absorberSoln, contaminants);

        var replacement = absorberSolution.SplitSolutionWithOnly(
            contaminants.Volume,
            Puddle.GetAbsorbentReagents(absorberSolution));

        if (replacement.Volume > 0 && puddle.Solution is { } puddleSolutionEnt)
            SolutionContainer.AddSolution(puddleSolutionEnt, replacement);

        return true;
    }
}
// VG-Tweak End
