using Content.Server.GameTicking.Events;
using Content.Shared.GameTicking.Events;
using Content.Shared.Chat;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Interaction.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Medical;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Repairable;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Server._VanGuard.CrewOmen;

/// <summary>
/// Collects small, round-only crew statistics and prints one harmless title per player
/// in the round-end player manifest. It only observes existing events and does not change
/// the behavior of the systems that produce them.
/// </summary>
public sealed partial class CrewOmenSystem : EntitySystem
{
    [Dependency] private SharedMindSystem _mind = default!;

    private const int BorgRepairsForRobotMaster = 2;
    private const int CareActionsForFieldDoctor = 3;
    private const int CareActionsForSurgeon = 8;
    private const int RecoveriesForUnbreakable = 2;
    private const int RadioMessagesForRadioVoice = 10;
    private const int MessagesForLoudVoice = 20;
    private const int MessagesForMasterCommunicator = 50;
    private const int DamageEventsForToughNut = 10;

    private static readonly string[] FallbackTitleKeys =
    {
        "crew-omen-title-on-duty",
        "crew-omen-title-duty-worker",
        "crew-omen-title-on-standby",
        "crew-omen-title-in-formation",
        "crew-omen-title-helping-hand",
        "crew-omen-title-station-worker",
        "crew-omen-title-shift-staff",
        "crew-omen-title-department-staff",
        "crew-omen-title-field-staff",
        "crew-omen-title-station-operator",
        "crew-omen-title-crew-staff",
        "crew-omen-title-regular-crew",
        "crew-omen-title-shift-specialist",
        "crew-omen-title-service-staff",
        "crew-omen-title-reserve",
        "crew-omen-title-on-watch",
        "crew-omen-title-station-hand",
        "crew-omen-title-team-member",
        "crew-omen-title-duty-specialist",
        "crew-omen-title-backup-crew"
    };

    private readonly Dictionary<EntityUid, CrewOmenStats> _stats = new();
    private EntityUid? _firstSpeaker;
    private EntityUid? _firstWhisperer;
    private EntityUid? _firstRadioSpeaker;
    private EntityUid? _firstRepairer;
    private EntityUid? _firstBorgRepairer;
    private EntityUid? _firstHelper;
    private EntityUid? _firstCritical;
    private EntityUid? _firstFallen;
    private EntityUid? _firstHugger;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(_ => Reset());
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());

        SubscribeLocalEvent<EntitySpokeEvent>(OnEntitySpoke);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<MobStateComponent, TargetDefibrillatedEvent>(OnDefibrillated);
        SubscribeLocalEvent<RepairableComponent, RepairedEvent>(OnRepaired);
        SubscribeLocalEvent<InteractionPopupComponent, InteractionSuccessEvent>(OnInteractionSuccess);
    }

    private void Reset()
    {
        _stats.Clear();
        _firstSpeaker = null;
        _firstWhisperer = null;
        _firstRadioSpeaker = null;
        _firstRepairer = null;
        _firstBorgRepairer = null;
        _firstHelper = null;
        _firstCritical = null;
        _firstFallen = null;
        _firstHugger = null;
    }

    private void OnEntitySpoke(EntitySpokeEvent args)
    {
        if (!TryGetPlayerMind(args.Source, out var mindId))
            return;

        var stats = GetStats(mindId);
        stats.Messages++;
        stats.HasSpoken = true;
        stats.MessageCharacters += args.Message.Length;
        stats.MessageWords += CountWords(args.Message);

        if (args.Message.Length <= 3)
            stats.TinyMessages++;
        else if (args.Message.Length <= 12)
            stats.ShortMessages++;
        else if (args.Message.Length >= 80)
            stats.LongMessages++;

        _firstSpeaker ??= mindId;

        if (args.ObfuscatedMessage != null)
        {
            stats.Whispers++;
            _firstWhisperer ??= mindId;
        }

        if (args.Channel == null)
            return;

        stats.RadioMessages++;
        stats.RadioChannelIds.Add(args.Channel.ID);
        _firstRadioSpeaker ??= mindId;
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (!TryGetPlayerMind(args.Target, out var mindId))
            return;

        var stats = GetStats(mindId);

        if (args.OldMobState == MobState.Critical && args.NewMobState == MobState.Alive)
            stats.CriticalRecoveries++;

        if (args.NewMobState == MobState.Critical && args.OldMobState != MobState.Critical)
        {
            _firstCritical ??= mindId;
        }

        if (args.NewMobState == MobState.Dead && args.OldMobState != MobState.Dead)
        {
            stats.Deaths++;
            _firstFallen ??= mindId;
        }
    }

    private void OnDamageChanged(DamageChangedEvent args)
    {
        var target = args.Damageable.Owner;
        if (!TryGetPlayerMind(target, out var targetMind))
            return;

        if (args.DamageIncreased)
        {
            if (args.DamageDelta is { } damage && damage.GetTotal() > 0)
                GetStats(targetMind).DamageEventsTaken++;

            return;
        }

        if (args.Origin is not { } origin
            || args.DamageDelta is not { } delta
            || delta.GetTotal() >= 0
            || origin == target
            || !TryGetPlayerMind(origin, out var healerMind)
            || healerMind == targetMind)
            return;

        var healerStats = GetStats(healerMind);
        healerStats.CareActions++;
        healerStats.HasHelpfulAction = true;
        _firstHelper ??= healerMind;
    }

    private void OnDefibrillated(EntityUid target, MobStateComponent component, ref TargetDefibrillatedEvent args)
    {
        if (!TryGetPlayerMind(args.User, out var mindId))
            return;

        var stats = GetStats(mindId);
        stats.CareActions++;
        stats.HasHelpfulAction = true;
        stats.Defibrillations++;
        _firstHelper ??= mindId;
    }

    private void OnRepaired(Entity<RepairableComponent> ent, ref RepairedEvent args)
    {
        var target = ent.Owner;
        if (target == args.User || !TryGetPlayerMind(args.User, out var mindId))
            return;

        var stats = GetStats(mindId);
        stats.Repairs++;
        stats.HasHelpfulAction = true;

        if (HasComp<BorgChassisComponent>(target))
        {
            stats.BorgRepairs++;
            _firstBorgRepairer ??= mindId;
        }

        _firstRepairer ??= mindId;
    }

    private void OnInteractionSuccess(EntityUid target, InteractionPopupComponent component, ref InteractionSuccessEvent args)
    {
        if (!TryGetPlayerMind(args.User, out var mindId))
            return;

        GetStats(mindId).SocialInteractions++;
        _firstHugger ??= mindId;
    }

    private static int CountWords(string message)
    {
        var words = 0;
        var inWord = false;

        foreach (var character in message)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (!inWord)
                    words++;

                inWord = true;
            }
            else
            {
                inWord = false;
            }
        }

        return words;
    }

    private CrewOmenStats GetStats(EntityUid mindId)
    {
        if (!_stats.TryGetValue(mindId, out var stats))
        {
            stats = new CrewOmenStats();
            _stats.Add(mindId, stats);
        }

        return stats;
    }

    public string GetTitle(EntityUid mindId)
    {
        return Loc.GetString(GetTitleCandidates(mindId)[0].Key);
    }

    public Dictionary<EntityUid, string> AssignTitles(IReadOnlyList<EntityUid> mindIds)
    {
        var candidates = new Dictionary<EntityUid, List<TitleCandidate>>();
        var nextCandidate = new Dictionary<EntityUid, int>();
        var remaining = new HashSet<EntityUid>(mindIds);
        var usedTitles = new HashSet<string>();
        var assignedTitles = new Dictionary<EntityUid, string>();

        foreach (var mindId in remaining)
        {
            candidates[mindId] = GetTitleCandidates(mindId);
            nextCandidate[mindId] = 0;
        }

        var fallbackNumber = 1;
        while (remaining.Count > 0)
        {
            var foundCandidate = false;
            var selectedMind = default(EntityUid);
            var selectedCandidate = default(TitleCandidate);

            foreach (var mindId in remaining)
            {
                var list = candidates[mindId];
                var index = nextCandidate[mindId];

                while (index < list.Count && usedTitles.Contains(list[index].Key))
                    index++;

                nextCandidate[mindId] = index;
                if (index >= list.Count)
                    continue;

                var candidate = list[index];
                if (foundCandidate
                    && (candidate.Priority > selectedCandidate.Priority
                        || candidate.Priority == selectedCandidate.Priority && candidate.Strength < selectedCandidate.Strength
                        || candidate.Priority == selectedCandidate.Priority && candidate.Strength == selectedCandidate.Strength
                        && mindId.CompareTo(selectedMind) >= 0))
                    continue;

                foundCandidate = true;
                selectedMind = mindId;
                selectedCandidate = candidate;
            }

            if (!foundCandidate)
            {
                var fallbackMind = default(EntityUid);
                foreach (var mindId in remaining)
                {
                    fallbackMind = mindId;
                    break;
                }

                var fallbackTitle = fallbackNumber <= FallbackTitleKeys.Length
                    ? Loc.GetString(FallbackTitleKeys[fallbackNumber - 1])
                    : Loc.GetString("crew-omen-title-extra-crew", ("number", fallbackNumber));

                assignedTitles[fallbackMind] = fallbackTitle;
                fallbackNumber++;
                remaining.Remove(fallbackMind);
                continue;
            }

            assignedTitles[selectedMind] = Loc.GetString(selectedCandidate.Key);
            usedTitles.Add(selectedCandidate.Key);
            remaining.Remove(selectedMind);
        }

        return assignedTitles;
    }

    private List<TitleCandidate> GetTitleCandidates(EntityUid mindId)
    {
        var result = new List<TitleCandidate>();
        var priority = 0;

        if (!_stats.TryGetValue(mindId, out var stats))
        {
            result.Add(new TitleCandidate("crew-omen-title-default", int.MaxValue, 0));
            return result;
        }

        AddCandidate(result, ref priority, "crew-omen-title-first-fallen", _firstFallen == mindId, stats.Deaths);
        AddCandidate(result, ref priority, "crew-omen-title-serial-survivor", stats.Deaths >= 5, stats.Deaths);
        AddCandidate(result, ref priority, "crew-omen-title-death-defier", stats.Deaths >= 2, stats.Deaths);
        AddCandidate(result, ref priority, "crew-omen-title-comeback-legend", stats.CriticalRecoveries >= 10, stats.CriticalRecoveries);
        AddCandidate(result, ref priority, "crew-omen-title-second-wind", stats.CriticalRecoveries >= 5, stats.CriticalRecoveries);
        AddCandidate(result, ref priority, "crew-omen-title-hard-to-kill", stats.CriticalRecoveries >= 3, stats.CriticalRecoveries);
        AddCandidate(result, ref priority, "crew-omen-title-unbreakable", stats.CriticalRecoveries >= RecoveriesForUnbreakable, stats.CriticalRecoveries);
        AddCandidate(result, ref priority, "crew-omen-title-first-critical", _firstCritical == mindId);
        AddCandidate(result, ref priority, "crew-omen-title-critical-survivor", stats.CriticalRecoveries >= 1, stats.CriticalRecoveries);
        AddCandidate(result, ref priority, "crew-omen-title-borg-engineer", stats.BorgRepairs >= 5, stats.BorgRepairs);
        AddCandidate(result, ref priority, "crew-omen-title-first-borg-repairer", _firstBorgRepairer == mindId);
        AddCandidate(result, ref priority, "crew-omen-title-robot-master", stats.BorgRepairs >= BorgRepairsForRobotMaster, stats.BorgRepairs);
        AddCandidate(result, ref priority, "crew-omen-title-first-helper", _firstHelper == mindId);
        AddCandidate(result, ref priority, "crew-omen-title-all-rounder", stats.Messages >= 5 && stats.Messages < 20
            && stats.Repairs >= 1 && stats.Repairs < 3 && stats.CareActions >= 1 && stats.CareActions < 3
            && stats.DamageEventsTaken == 0);
        AddCandidate(result, ref priority, "crew-omen-title-frontline-medic", stats.CareActions >= 20 && stats.DamageEventsTaken >= 5, stats.CareActions);
        AddCandidate(result, ref priority, "crew-omen-title-combat-medic", stats.CareActions >= 20, stats.CareActions);
        AddCandidate(result, ref priority, "crew-omen-title-fate-surgeon", stats.CareActions >= CareActionsForSurgeon, stats.CareActions);
        AddCandidate(result, ref priority, "crew-omen-title-life-saver", stats.CareActions >= 12, stats.CareActions);
        AddCandidate(result, ref priority, "crew-omen-title-defib-specialist", stats.Defibrillations >= 3, stats.Defibrillations);
        AddCandidate(result, ref priority, "crew-omen-title-nurse", stats.CareActions >= 5, stats.CareActions);
        AddCandidate(result, ref priority, "crew-omen-title-rapid-response", stats.CareActions >= 1 && stats.DamageEventsTaken >= 5, stats.CareActions);
        AddCandidate(result, ref priority, "crew-omen-title-field-doctor", stats.CareActions >= CareActionsForFieldDoctor, stats.CareActions);
        AddCandidate(result, ref priority, "crew-omen-title-good-samaritan", stats.CareActions >= 1, stats.CareActions);
        AddCandidate(result, ref priority, "crew-omen-title-repair-machine", stats.Repairs >= 75, stats.Repairs);
        AddCandidate(result, ref priority, "crew-omen-title-chief-engineer", stats.Repairs >= 25, stats.Repairs);
        AddCandidate(result, ref priority, "crew-omen-title-station-mechanic", stats.Repairs >= 10, stats.Repairs);
        AddCandidate(result, ref priority, "crew-omen-title-golden-hands", stats.Repairs >= 5, stats.Repairs);
        AddCandidate(result, ref priority, "crew-omen-title-shift-worker", stats.Repairs >= 3 && stats.Messages >= 5, stats.Repairs);
        AddCandidate(result, ref priority, "crew-omen-title-repair-enthusiast", stats.Repairs >= 3, stats.Repairs);
        AddCandidate(result, ref priority, "crew-omen-title-first-repairer", _firstRepairer == mindId);
        AddCandidate(result, ref priority, "crew-omen-title-apprentice-mechanic", stats.Repairs >= 1, stats.Repairs);
        AddCandidate(result, ref priority, "crew-omen-title-veteran", stats.Messages >= 10 && stats.DamageEventsTaken >= 5, stats.Messages);
        AddCandidate(result, ref priority, "crew-omen-title-walking-fortress", stats.DamageEventsTaken >= 50, stats.DamageEventsTaken);
        AddCandidate(result, ref priority, "crew-omen-title-damage-magnet", stats.DamageEventsTaken >= 25, stats.DamageEventsTaken);
        AddCandidate(result, ref priority, "crew-omen-title-steel-nerves", stats.DamageEventsTaken >= 15, stats.DamageEventsTaken);
        AddCandidate(result, ref priority, "crew-omen-title-tough-nut", stats.DamageEventsTaken >= DamageEventsForToughNut, stats.DamageEventsTaken);
        AddCandidate(result, ref priority, "crew-omen-title-battle-scarred", stats.DamageEventsTaken >= 5, stats.DamageEventsTaken);
        AddCandidate(result, ref priority, "crew-omen-title-bruised", stats.DamageEventsTaken >= 1, stats.DamageEventsTaken);
        AddCandidate(result, ref priority, "crew-omen-title-station-orator", stats.Messages >= 250, stats.Messages);
        AddCandidate(result, ref priority, "crew-omen-title-endless-voice", stats.Messages >= 100, stats.Messages);
        AddCandidate(result, ref priority, "crew-omen-title-master-communicator", stats.Messages >= MessagesForMasterCommunicator, stats.Messages);
        AddCandidate(result, ref priority, "crew-omen-title-loud-voice", stats.Messages >= MessagesForLoudVoice, stats.Messages);
        AddCandidate(result, ref priority, "crew-omen-title-wordsmith", stats.MessageWords >= 100, stats.MessageWords);
        AddCandidate(result, ref priority, "crew-omen-title-chronicler", stats.MessageCharacters >= 1000, stats.MessageCharacters);
        AddCandidate(result, ref priority, "crew-omen-title-detailed-reporter", stats.LongMessages >= 25, stats.LongMessages);
        AddCandidate(result, ref priority, "crew-omen-title-long-winded", stats.LongMessages >= 10, stats.LongMessages);
        AddCandidate(result, ref priority, "crew-omen-title-message-sprinter", stats.TinyMessages >= 25, stats.TinyMessages);
        AddCandidate(result, ref priority, "crew-omen-title-brief-communicator", stats.ShortMessages >= 10, stats.ShortMessages);
        AddCandidate(result, ref priority, "crew-omen-title-first-whisperer", _firstWhisperer == mindId);
        AddCandidate(result, ref priority, "crew-omen-title-secret-keeper", stats.Whispers >= 20, stats.Whispers);
        AddCandidate(result, ref priority, "crew-omen-title-whisperer", stats.Whispers >= 5, stats.Whispers);
        AddCandidate(result, ref priority, "crew-omen-title-radio-marathoner", stats.RadioMessages >= 150, stats.RadioMessages);
        AddCandidate(result, ref priority, "crew-omen-title-radio-addict", stats.RadioMessages >= 75, stats.RadioMessages);
        AddCandidate(result, ref priority, "crew-omen-title-radio-regular", stats.RadioMessages >= 25, stats.RadioMessages);
        AddCandidate(result, ref priority, "crew-omen-title-channel-collector", stats.RadioChannelIds.Count >= 5, stats.RadioChannelIds.Count);
        AddCandidate(result, ref priority, "crew-omen-title-frequency-explorer", stats.RadioChannelIds.Count >= 3, stats.RadioChannelIds.Count);
        AddCandidate(result, ref priority, "crew-omen-title-first-radio", _firstRadioSpeaker == mindId);
        AddCandidate(result, ref priority, "crew-omen-title-radio-voice", stats.RadioMessages >= RadioMessagesForRadioVoice, stats.RadioMessages);
        AddCandidate(result, ref priority, "crew-omen-title-first-voice", _firstSpeaker == mindId);
        AddCandidate(result, ref priority, "crew-omen-title-cuddle-officer", stats.SocialInteractions >= 50, stats.SocialInteractions);
        AddCandidate(result, ref priority, "crew-omen-title-social-butterfly", stats.SocialInteractions >= 15, stats.SocialInteractions);
        AddCandidate(result, ref priority, "crew-omen-title-friendly", stats.SocialInteractions >= 5, stats.SocialInteractions);
        AddCandidate(result, ref priority, "crew-omen-title-first-hugger", _firstHugger == mindId);
        AddCandidate(result, ref priority, "crew-omen-title-silent-hero", stats.HasHelpfulAction && !stats.HasSpoken);
        AddCandidate(result, ref priority, "crew-omen-title-default", true);

        return result;
    }

    private static void AddCandidate(List<TitleCandidate> candidates, ref int priority, string key, bool condition, int strength = 0)
    {
        if (condition)
            candidates.Add(new TitleCandidate(key, priority, strength));

        priority++;
    }

    private bool TryGetPlayerMind(EntityUid uid, out EntityUid mindId)
    {
        mindId = default;
        if (!_mind.TryGetMind(uid, out var foundMindId, out var mind)
            || (mind.UserId == null && mind.OriginalOwnerUserId == null))
            return false;

        mindId = foundMindId;
        return true;
    }

    private readonly record struct TitleCandidate(string Key, int Priority, int Strength);

    private sealed class CrewOmenStats
    {
        public int Messages;
        public int RadioMessages;
        public int Whispers;
        public int MessageWords;
        public int MessageCharacters;
        public int TinyMessages;
        public int ShortMessages;
        public int LongMessages;
        public int Repairs;
        public int BorgRepairs;
        public int CareActions;
        public int Defibrillations;
        public int CriticalRecoveries;
        public int Deaths;
        public int DamageEventsTaken;
        public int SocialInteractions;
        public bool HasSpoken;
        public bool HasHelpfulAction;
        public readonly HashSet<string> RadioChannelIds = new();
    }
}