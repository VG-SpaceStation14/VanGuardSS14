using Content.Shared.Power.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Power;
using Content.Shared.PowerCell.Components;
using Robust.Client.GameObjects;

namespace Content.Client.PowerCell;

public sealed partial class PowerChargerVisualizerSystem : VisualizerSystem<PowerChargerVisualsComponent>
{
    [Dependency] private ItemSlotsSystem _itemSlots = default!;

    protected override void OnAppearanceChange(EntityUid uid, PowerChargerVisualsComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        // VG-Tweak Start
        bool hasCell = _itemSlots.TryGetSlot(uid, "charger_slot", out var slot) && slot.Item != null;

        string baseState;
        if (hasCell)
        {
            string stateToUse = comp.OccupiedState;
            if (slot!.Item is { } cellUid)
            {
                if (TryComp(cellUid, out MetaDataComponent? meta) && meta != null && meta.EntityPrototype != null)
                {
                    var proto = meta.EntityPrototype;
                    if (comp.CellStates.TryGetValue(proto.ID, out var customState))
                    {
                        stateToUse = customState;
                    }
                    else if (proto.Parents != null)
                    {
                        foreach (var parentId in proto.Parents)
                        {
                            if (comp.CellStates.TryGetValue(parentId, out var parentState))
                            {
                                stateToUse = parentState;
                                break;
                            }
                        }
                    }
                }
            }
            baseState = stateToUse;
        // VG-Tweak End
        }
        else
        {
            baseState = comp.EmptyState;
        }
        SpriteSystem.LayerSetRsiState((uid, args.Sprite), PowerChargerVisualLayers.Base, baseState);

        string lightState;
        if (!hasCell)
        {
            lightState = comp.LightStates.GetValueOrDefault(CellChargerStatus.Empty, "light-empty");
        }
        else
        {   
            // VG-Tweak Start
            if (AppearanceSystem.TryGetData<CellChargerStatus>(uid, CellVisual.Light, out var status, args.Component)
                && comp.LightStates.TryGetValue(status, out var state))
            {
                lightState = state;
            }
            else
            {
                lightState = comp.LightStates.GetValueOrDefault(CellChargerStatus.Off, "light-off");
            }
        }

        SpriteSystem.LayerSetRsiState((uid, args.Sprite), PowerChargerVisualLayers.Light, lightState);
        SpriteSystem.LayerSetVisible((uid, args.Sprite), PowerChargerVisualLayers.Light, true);
        // VG-Tweak End
    }
}

public enum PowerChargerVisualLayers : byte
{
    Base,
    Light,
}