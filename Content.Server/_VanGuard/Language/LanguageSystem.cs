using System.Linq;
using System.Text;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Events;
using Content.Server.Mind;
using Content.Shared._VanGuard.Language;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._VanGuard.Language;

public sealed partial class LanguageSystem : SharedLanguageSystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>
    ///     Round-wide seed used to keep obfuscation stable inside a single round.
    /// </summary>
    public int Seed { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LanguageSpeakerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
        SubscribeNetworkEvent<LanguageChosenMessage>(OnLanguageChosen);
    }

    private void OnMapInit(EntityUid uid, LanguageSpeakerComponent component, MapInitEvent args)
    {
        // Apply the species' default languages to species-based mobs, so that non-player
        // mobs (e.g. admin-spawned MobVulpkanin) speak their species' native tongue too.
        // Player mobs also get their languages through OnPlayerSpawned.
        if (TryComp<HumanoidProfileComponent>(uid, out var profile)
            && _proto.TryIndex<SpeciesPrototype>(profile.Species, out var species))
        {
            foreach (var language in species.DefaultLanguages)
            {
                component.Languages.TryAdd(language, LanguageKnowledge.Speak);
            }
        }

        if (component.CurrentLanguage == null)
        {
            SelectDefaultLanguage(uid, component);
        }

        RefreshUi(uid, component);
    }

    private void OnRoundStarting(RoundStartingEvent args)
    {
        Seed = _random.Next();
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        var speaker = EnsureComp<LanguageSpeakerComponent>(args.Mob);

        if (_proto.TryIndex<SpeciesPrototype>(args.Profile.Species, out var species))
        {
            foreach (var language in species.DefaultLanguages)
            {
                speaker.Languages[language] = LanguageKnowledge.Speak;
            }
        }

        foreach (var language in args.Profile.Languages)
        {
            speaker.Languages[language] = LanguageKnowledge.Speak;
        }

        SelectDefaultLanguage(args.Mob, speaker);
        RefreshUi(args.Mob, speaker);
    }

    private void OnLanguageChosen(LanguageChosenMessage args)
    {
        var uid = GetEntity(args.Uid);
        if (!TryComp<LanguageSpeakerComponent>(uid, out var component))
            return;

        // Only languages the entity can actually speak may be selected.
        if (!CanSpeak(uid, args.SelectedLanguage))
            return;

        component.CurrentLanguage = args.SelectedLanguage;
        RefreshUi(uid, component);
    }

    public override void RefreshUi(EntityUid uid, LanguageSpeakerComponent? comp = null)
    {
        base.RefreshUi(uid, comp);

        if (!Resolve(uid, ref comp, false))
            return;

        Dirty(uid, comp);

        if (!RetrieveKnownLanguages(uid, LanguageKnowledge.Understand, out var langs, out var translator))
            return;

        if (!_mind.TryGetMind(uid, out _, out var mind) || mind == null
            || !_players.TryGetSessionById(mind.UserId, out var session))
        {
            return;
        }

        // Languages that are not marked as understood-visible are hidden unless fully spoken.
        foreach (var (id, _) in langs.ToList())
        {
            var proto = _proto.Index<LanguagePrototype>(id);
            if (!proto.ShowUnderstood && langs[id] < LanguageKnowledge.BadSpeak)
                langs.Remove(id);
        }

        var current = comp.CurrentLanguage ?? CommonLanguageId;
        var state = new LanguageMenuStateMessage(GetNetEntity(uid), current, langs, translator);
        RaiseNetworkEvent(state, session);
    }
}
