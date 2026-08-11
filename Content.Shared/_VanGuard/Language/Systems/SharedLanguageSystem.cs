using System.Linq;
using Content.Shared.Ghost.Components;
using Content.Shared.Implants.Components;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.Language;

public abstract partial class SharedLanguageSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    /// <summary>
    ///     Fallback language used whenever an entity has no explicit selection.
    /// </summary>
    [ValidatePrototypeId<LanguagePrototype>]
    public static readonly string CommonLanguageId = "GalacticCommon";

    /// <summary>
    ///     Universal language understood by every entity.
    /// </summary>
    [ValidatePrototypeId<LanguagePrototype>]
    public static readonly string UniversalLanguageId = "Universal";

    public override void Initialize()
    {
        SubscribeLocalEvent<LanguageSpeakerComponent, GetLanguagesEvent>(OnCollectLanguages);
    }

    private void OnCollectLanguages(EntityUid uid, LanguageSpeakerComponent comp, GetLanguagesEvent args)
    {
        args.Current = comp.CurrentLanguage ?? CommonLanguageId;
        args.Languages = comp.Languages;

        // Implants can contribute their own languages.
        if (_container.TryGetContainer(uid, ImplanterComponent.ImplantSlotId, out var implantContainer))
        {
            foreach (var implant in implantContainer.ContainedEntities)
            {
                RaiseLocalEvent(implant, args);
            }
        }
    }

    public LanguagePrototype? FindLanguage(string id)
    {
        return _proto.TryIndex<LanguagePrototype>(id, out var proto) ? proto : null;
    }

    public LanguagePrototype GetSelectedLanguage(EntityUid uid)
    {
        if (TryComp<LanguageSpeakerComponent>(uid, out var comp) && comp.CurrentLanguage != null
            && _proto.TryIndex<LanguagePrototype>(comp.CurrentLanguage, out var selected))
        {
            return selected;
        }

        return _proto.Index<LanguagePrototype>(CommonLanguageId);
    }

    public bool CanSpeak(EntityUid uid, LanguagePrototype proto)
    {
        return CanSpeak(uid, proto.ID);
    }

    public bool CanSpeak(EntityUid uid, string protoId)
    {
        if (!_proto.TryIndex<LanguagePrototype>(protoId, out var proto))
            return false;

        if (HasComp<GhostComponent>(uid))
            return false;

        if (HasComp<UniversalLanguageSpeakerComponent>(uid))
            return true;

        if (proto.ID == UniversalLanguageId)
            return true;

        if (!RetrieveKnownLanguages(uid, LanguageKnowledge.BadSpeak, out var langs, out _))
            return false;

        return langs.ContainsKey(protoId);
    }

    public bool CanUnderstand(EntityUid uid, LanguagePrototype proto)
    {
        return CanUnderstand(uid, proto.ID);
    }

    public bool CanUnderstand(EntityUid uid, string protoId)
    {
        if (!_proto.TryIndex<LanguagePrototype>(protoId, out var proto))
            return false;

        if (HasComp<GhostComponent>(uid))
            return true;

        if (HasComp<UniversalLanguageSpeakerComponent>(uid))
            return true;

        if (proto.ID == UniversalLanguageId)
            return true;

        if (!RetrieveKnownLanguages(uid, LanguageKnowledge.Understand, out var langs, out _))
            return false;

        return langs.ContainsKey(protoId);
    }

    /// <summary>
    ///     Collects every language the entity knows, merging direct knowledge with translator knowledge.
    /// </summary>
    public bool RetrieveKnownLanguages(
        EntityUid uid,
        LanguageKnowledge minimum,
        out Dictionary<string, LanguageKnowledge> langs,
        out Dictionary<string, LanguageKnowledge> translator)
    {
        langs = new Dictionary<string, LanguageKnowledge>();
        translator = new Dictionary<string, LanguageKnowledge>();

        if (!HasComp<LanguageSpeakerComponent>(uid) && !HasComp<UniversalLanguageSpeakerComponent>(uid))
            return false;

        var ev = new GetLanguagesEvent();
        RaiseLocalEvent(uid, ev);

        foreach (var (id, knowledge) in ev.Languages)
        {
            if (knowledge >= minimum)
                langs[id] = knowledge;
        }

        foreach (var (id, knowledge) in ev.Translator)
        {
            if (knowledge < minimum)
                continue;

            if (langs.TryGetValue(id, out var direct) && direct > knowledge)
                continue;

            langs[id] = knowledge;
            translator[id] = knowledge;
        }

        return langs.Count > 0;
    }

    public void GrantLanguage(EntityUid uid, string lang, LanguageKnowledge knowledge = LanguageKnowledge.Speak, LanguageSpeakerComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return;

        if (!_proto.TryIndex<LanguagePrototype>(lang, out _))
            return;

        comp.Languages[lang] = knowledge;
        RefreshUi(uid, comp);
    }

    public void RevokeLanguage(EntityUid uid, string lang, LanguageSpeakerComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return;

        comp.Languages.Remove(lang);
        RefreshUi(uid, comp);
    }

    /// <summary>
    ///     Picks the most fluent known language as the current one, preferring
    ///     round-start languages and higher priority.
    /// </summary>
    public void SelectDefaultLanguage(EntityUid uid, LanguageSpeakerComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return;

        if (comp.CurrentLanguage != null && CanSpeak(uid, comp.CurrentLanguage))
            return;

        var best = comp.Languages
            .Where(x => x.Value >= LanguageKnowledge.BadSpeak)
            .OrderByDescending(x => x.Value)
            .ThenByDescending(x => _proto.Index<LanguagePrototype>(x.Key).Priority)
            .Select(x => x.Key)
            .FirstOrDefault();

        comp.CurrentLanguage = best ?? CommonLanguageId;
        RefreshUi(uid, comp);
    }

    /// <summary>
    ///     Orders the known languages by fluency, then priority, then name.
    /// </summary>
    public void OrderLanguages(EntityUid uid, LanguageSpeakerComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return;

        var ordered = comp.Languages
            .OrderByDescending(x => CanSpeak(uid, x.Key))
            .ThenByDescending(x => x.Value)
            .ThenByDescending(x => _proto.Index<LanguagePrototype>(x.Key).Priority)
            .ThenBy(x => _proto.Index<LanguagePrototype>(x.Key).Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Value);

        comp.Languages = ordered;
    }

    public virtual void RefreshUi(EntityUid uid, LanguageSpeakerComponent? comp = null)
    {
        OrderLanguages(uid);
    }
}
