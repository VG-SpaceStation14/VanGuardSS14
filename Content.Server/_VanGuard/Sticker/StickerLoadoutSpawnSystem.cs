using Content.Shared.Access.Components;
using Content.Shared.Clothing;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared._VanGuard.Sticker;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Sticker;

/// <summary>
/// VG-Tweak: Applies the sticker picked through <see cref="StickerLoadoutEffect"/> to the ID card of a spawned player.
/// </summary>
/// <remarks>
/// This used to live in StationSpawningSystem. That system was moved to Content.Shared upstream, so the logic
/// (which needs the server-only <see cref="StickerSystem"/>) was moved here and is now driven by
/// <see cref="PlayerSpawnCompleteEvent"/> instead.
/// </remarks>
public sealed partial class StickerLoadoutSpawnSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private StickerSystem _sticker = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId == null)
            return;

        // The loadout prototype the spawning code used for this job, see LoadoutSystem.GetJobPrototype.
        var jobLoadout = LoadoutSystem.GetJobPrototype(args.JobId);
        if (!ProtoMan.TryIndex<RoleLoadoutPrototype>(jobLoadout, out _))
            return;

        if (!args.Profile.Loadouts.TryGetValue(jobLoadout, out var loadout))
        {
            loadout = new RoleLoadout(jobLoadout);
            loadout.SetDefault(args.Profile, args.Player, ProtoMan);
        }

        foreach (var (_, loadoutList) in loadout.SelectedLoadouts)
        {
            foreach (var selected in loadoutList)
            {
                if (!ProtoMan.TryIndex<LoadoutPrototype>(selected.Prototype, out var loadoutProto))
                    continue;

                foreach (var effect in loadoutProto.Effects)
                {
                    if (effect is not StickerLoadoutEffect stickerEffect)
                        continue;

                    var sticker = Spawn(stickerEffect.StickerProto, Transform(args.Mob).Coordinates);
                    Timer.Spawn(TimeSpan.Zero, () => TryApplySticker(args.Mob, sticker));
                }
            }
        }
    }

    private void TryApplySticker(EntityUid mob, EntityUid sticker)
    {
        if (Deleted(mob) || Deleted(sticker))
        {
            if (!Deleted(sticker))
                Del(sticker);

            return;
        }

        EntityUid? idCard = null;
        if (_inventory.TryGetSlotEntity(mob, "id", out var idSlotEntity))
        {
            if (TryComp<PdaComponent>(idSlotEntity, out var pda) && pda.ContainedId != null)
                idCard = pda.ContainedId.Value;
            else if (HasComp<IdCardComponent>(idSlotEntity))
                idCard = idSlotEntity;
        }

        if (idCard != null)
        {
            _sticker.SetSticker(idCard.Value, sticker);
            return;
        }

        // Nowhere to stick it to, clean the sticker up again.
        Del(sticker);
    }
}
