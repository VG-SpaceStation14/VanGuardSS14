using Content.Shared._VanGuard.CCVars;
using Content.Shared._VanGuard.Examine;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._VanGuard.Examine;

/// <summary>
/// Adds a compact equipment breakdown to a character's examine text when the examiner has the detailed
/// examine option enabled. Items hidden by outer clothing are only listed when no outer clothing is worn.
/// </summary>
public sealed partial class ExaminableCharacterSystem : EntitySystem
{
    private const string HeadSlot = "head";
    private const string OuterSlot = "outerClothing";
    private const string EarsSlot = "ears";

    private static readonly string[] AlwaysVisibleSlots = { "eyes", "mask", "neck", "back", "suitstorage" };
    private static readonly string[] UnderOuterSlots = { "jumpsuit", "gloves", "belt", "id", "shoes" };

    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private INetConfigurationManager _config = default!;
    [Dependency] private IEntityManager _entities = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExaminableCharacterComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, ExaminableCharacterComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange
            || !TryComp<ActorComponent>(args.Examiner, out var actor)
            || !_config.GetClientCVar(actor.PlayerSession.Channel, VGCCVars.DetailedExamine))
            return;

        var self = args.Examiner == args.Examined;
        AddHeader(uid, args, self);

        var priority = 13;
        var shownAny = false;

        if (TryGetWornItem(uid, OuterSlot, out var outerName))
        {
            shownAny = true;
            PushItemLine(args, self ? "outer-examine-selfaware" : "outer-examine", outerName, priority--);
        }

        if (TryGetWornItem(uid, HeadSlot, out var headName))
        {
            shownAny = true;
            PushItemLine(args, self ? "head-examine-selfaware" : "head-examine", headName, priority--);
        }

        foreach (var slot in AlwaysVisibleSlots)
        {
            if (TryGetWornItem(uid, slot, out var itemName))
            {
                shownAny = true;
                PushItemLine(args, SlotLocKey(slot, self), itemName, priority--);
            }
        }

        var hasHead = _inventory.TryGetSlotEntity(uid, HeadSlot, out _);
        if (!hasHead && TryGetWornItem(uid, EarsSlot, out var earsName))
        {
            shownAny = true;
            PushItemLine(args, self ? "ears-examine-selfaware" : "ears-examine", earsName, priority--);
        }

        var hasOuter = _inventory.TryGetSlotEntity(uid, OuterSlot, out _);
        if (!hasOuter)
        {
            foreach (var slot in UnderOuterSlots)
            {
                if (!TryGetWornItem(uid, slot, out var itemName))
                    continue;

                shownAny = true;
                PushItemLine(args, SlotLocKey(slot, self), itemName, priority--);
            }
        }

        if (shownAny)
            return;

        var emptyKey = self ? "examine-can-see-nothing-selfaware" : "examine-can-see-nothing";
        args.PushMarkup($"[font size=10]{Loc.GetString(emptyKey, ("ent", uid))}[/font]", 13);
    }

    private void AddHeader(EntityUid examined, ExaminedEvent args, bool self)
    {
        var name = self
            ? Loc.GetString("examine-name-selfaware")
            : Loc.GetString("examine-name", ("name", Identity.Entity(examined, _entities)));

        args.PushMarkup($"[font size=11]{name}[/font]", 15);

        var canSee = self
            ? Loc.GetString("examine-can-see-selfaware", ("ent", examined))
            : Loc.GetString("examine-can-see", ("ent", examined));

        args.PushMarkup($"[font size=10]{canSee}[/font]", 14);
    }

    private bool TryGetWornItem(EntityUid wearer, string slot, out string itemName)
    {
        itemName = string.Empty;

        if (!_inventory.TryGetSlotEntity(wearer, slot, out var item)
            || !TryComp<MetaDataComponent>(item, out var meta))
            return false;

        itemName = meta.EntityName;
        return true;
    }

    private static string SlotLocKey(string slot, bool self)
    {
        return self ? $"{slot}-examine-selfaware" : $"{slot}-examine";
    }

    private void PushItemLine(ExaminedEvent args, string locKey, string itemName, int priority)
    {
        args.PushMarkup($"[font size=10]{Loc.GetString(locKey, ("item", itemName))}[/font]", priority);
    }
}
