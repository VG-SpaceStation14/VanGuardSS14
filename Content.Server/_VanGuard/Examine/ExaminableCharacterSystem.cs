using Content.Shared._VanGuard.CCVars;
using Content.Shared._VanGuard.Examine;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._VanGuard.Examine;

public sealed partial class ExaminableCharacterSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private INetConfigurationManager _netConfig = default!;
    [Dependency] private IEntityManager _entityManager = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExaminableCharacterComponent, ExaminedEvent>(OnExamineCharacter);
    }

    private void OnExamineCharacter(EntityUid uid, ExaminableCharacterComponent comp, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!TryComp<ActorComponent>(args.Examiner, out var actor))
            return;

        var detailed = _netConfig.GetClientCVar(actor.PlayerSession.Channel, VGCCVars.DetailedExamine);
        if (!detailed)
            return;

        var selfAware = args.Examiner == args.Examined;

        string nameLine;
        if (selfAware)
        {
            nameLine = Loc.GetString("examine-name-selfaware");
        }
        else
        {
            var identity = Identity.Entity(uid, _entityManager);
            nameLine = Loc.GetString("examine-name", ("name", identity));
        }
        args.PushMarkup($"[font size=11]{nameLine}[/font]", 15);

        var canSee = selfAware
            ? Loc.GetString("examine-can-see-selfaware", ("ent", uid))
            : Loc.GetString("examine-can-see", ("ent", uid));
        args.PushMarkup($"[font size=10]{canSee}[/font]", 14);

        bool hasOuter = _inventory.TryGetSlotEntity(uid, "outerClothing", out _);
        bool hasHelmet = _inventory.TryGetSlotEntity(uid, "head", out _);

        var alwaysShowSlots = new Dictionary<string, string>
        {
            { "head", "head" },
            { "eyes", "eyes" },
            { "mask", "mask" },
            { "neck", "neck" },
            { "back", "back" },
            { "suitstorage", "suitstorage" }
        };

        var hiddenIfOuterSlots = new Dictionary<string, string>
        {
            { "jumpsuit", "jumpsuit" },
            { "gloves", "gloves" },
            { "belt", "belt" },
            { "id", "id" },
            { "shoes", "shoes" }
        };

        int priority = 13;
        bool hasAnyItem = false;

        if (hasOuter && _inventory.TryGetSlotEntity(uid, "outerClothing", out var outerItem) &&
            TryComp<MetaDataComponent>(outerItem, out var outerMeta))
        {
            hasAnyItem = true;
            var locKey = selfAware ? "outer-examine-selfaware" : "outer-examine";
            var line = Loc.GetString(locKey, ("item", outerMeta.EntityName));
            args.PushMarkup($"[font size=10]{line}[/font]", priority);
            priority--;
        }

        if (hasHelmet && _inventory.TryGetSlotEntity(uid, "head", out var helmetItem) &&
            TryComp<MetaDataComponent>(helmetItem, out var helmetMeta))
        {
            hasAnyItem = true;
            var locKey = selfAware ? "head-examine-selfaware" : "head-examine";
            var line = Loc.GetString(locKey, ("item", helmetMeta.EntityName));
            args.PushMarkup($"[font size=10]{line}[/font]", priority);
            priority--;
        }

        foreach (var slot in alwaysShowSlots)
        {
            if (slot.Key == "head")
                continue;

            if (!_inventory.TryGetSlotEntity(uid, slot.Key, out var item))
                continue;
            if (!TryComp<MetaDataComponent>(item, out var meta))
                continue;

            hasAnyItem = true;
            var locKey = slot.Value + "-examine";
            if (selfAware)
                locKey += "-selfaware";

            var line = Loc.GetString(locKey, ("item", meta.EntityName));
            args.PushMarkup($"[font size=10]{line}[/font]", priority);
            priority--;
        }

        if (!hasHelmet)
        {
            if (_inventory.TryGetSlotEntity(uid, "ears", out var earsItem) &&
                TryComp<MetaDataComponent>(earsItem, out var earsMeta))
            {
                hasAnyItem = true;
                var locKey = selfAware ? "ears-examine-selfaware" : "ears-examine";
                var line = Loc.GetString(locKey, ("item", earsMeta.EntityName));
                args.PushMarkup($"[font size=10]{line}[/font]", priority);
                priority--;
            }
        }

        if (!hasOuter)
        {
            foreach (var slot in hiddenIfOuterSlots)
            {
                if (!_inventory.TryGetSlotEntity(uid, slot.Key, out var item))
                    continue;
                if (!TryComp<MetaDataComponent>(item, out var meta))
                    continue;

                hasAnyItem = true;
                var locKey = slot.Value + "-examine";
                if (selfAware)
                    locKey += "-selfaware";

                var line = Loc.GetString(locKey, ("item", meta.EntityName));
                args.PushMarkup($"[font size=10]{line}[/font]", priority);
                priority--;
            }
        }

        if (!hasAnyItem)
        {
            var nothing = selfAware
                ? Loc.GetString("examine-can-see-nothing-selfaware", ("ent", uid))
                : Loc.GetString("examine-can-see-nothing", ("ent", uid));
            args.PushMarkup($"[font size=10]{nothing}[/font]", 13);
        }
    }
}