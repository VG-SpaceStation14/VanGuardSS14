using Content.Shared.GameTicking;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Content.Shared.Whitelist;

namespace Content.Server.Traits;

public sealed partial class TraitSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _sharedHandsSystem = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    // When the player is spawned in, add all trait components selected during character creation
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        // Check if player's job allows to apply traits
        if (args.JobId == null ||
            !ProtoMan.Resolve<JobPrototype>(args.JobId, out var protoJob) ||
            !protoJob.ApplyTraits)
        {
            return;
        }

        foreach (var traitId in args.Profile.TraitPreferences)
        {
            if (!ProtoMan.TryIndex<TraitPrototype>(traitId, out var traitPrototype))
            {
                Log.Error($"No trait found with ID {traitId}!");
                return;
            }

            if (_whitelistSystem.IsWhitelistFail(traitPrototype.Whitelist, args.Mob) ||
                _whitelistSystem.IsWhitelistPass(traitPrototype.Blacklist, args.Mob))
                continue;

            // VG-Tweak Start: skip quirks that are not available for the character's species or that conflict with
            // another selected trait. Profile validation normally removes these, this is a safety net for bad data.
            if (traitPrototype.SpeciesBlacklist.Contains(args.Profile.Species) ||
                (traitPrototype.SpeciesWhitelist.Count > 0 &&
                 !traitPrototype.SpeciesWhitelist.Contains(args.Profile.Species)))
            {
                continue;
            }

            var hasConflict = false;
            foreach (var otherId in args.Profile.TraitPreferences)
            {
                if (otherId == traitId || !ProtoMan.TryIndex(otherId, out var otherTrait))
                    continue;

                if (otherTrait.Conflicts.Contains(traitPrototype.ID) || traitPrototype.Conflicts.Contains(otherTrait.ID))
                {
                    hasConflict = true;
                    break;
                }
            }

            if (hasConflict)
                continue;
            // VG-Tweak End

            // Add all components required by the prototype
#pragma warning disable CS0618 // Enabling compatibility behaviour for TraitPrototype.Components
            if (traitPrototype.Components.Count > 0)
                EntityManager.AddComponents(args.Mob, traitPrototype.Components, false);
#pragma warning restore CS0618

            // Add all JobSpecials required by the prototype
            foreach (var special in traitPrototype.Specials)
            {
                special.AfterEquip(args.Mob);
            }

            // Add item required by the trait
            if (traitPrototype.TraitGear == null)
                continue;

            if (!TryComp(args.Mob, out HandsComponent? handsComponent))
                continue;

            var coords = Transform(args.Mob).Coordinates;
            var inhandEntity = Spawn(traitPrototype.TraitGear, coords);
            _sharedHandsSystem.TryPickup(args.Mob,
                inhandEntity,
                checkActionBlocker: false,
                handsComp: handsComponent);
        }
    }
}
