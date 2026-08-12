using Content.Shared._VanGuard.Language;
using Content.Shared.ActionBlocker;
using Content.Shared.Examine;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;

namespace Content.Server._VanGuard.Language;

/// <summary>
///     The speaker must be able to speak for the message to be sent.
/// </summary>
public sealed partial class SpeakerActiveCondition : LanguageCondition
{
    public override bool Evaluate(EntityUid target, EntityUid? source, IEntityManager entMan)
    {
        return entMan.System<ActionBlockerSystem>().CanSpeak(target);
    }
}

/// <summary>
///     The speaker must be able to emote for the message to be sent.
/// </summary>
public sealed partial class EmoteSpeakerCondition : LanguageCondition
{
    public override bool Evaluate(EntityUid target, EntityUid? source, IEntityManager entMan)
    {
        return entMan.System<ActionBlockerSystem>().CanEmote(target);
    }
}

/// <summary>
///     The listener must not be blocked from hearing. Used by vocal languages.
/// </summary>
public sealed partial class ListenerHearingCondition : LanguageCondition
{
    public override bool Evaluate(EntityUid target, EntityUid? source, IEntityManager entMan)
    {
        return !entMan.HasComponent<BlockListeningComponent>(target);
    }
}

/// <summary>
///     The listener must be able to see the speaker. Used by sign/emote languages.
/// </summary>
public sealed partial class ListenerVisionCondition : LanguageCondition
{
    /// <summary>
    ///     Maximum range at which sign language can be followed.
    /// </summary>
    private const float SignLanguageRange = 8f;

    public override bool Evaluate(EntityUid target, EntityUid? source, IEntityManager entMan)
    {
        // Entities without eyes cannot follow sign languages.
        if (!entMan.HasComponent<Robust.Shared.GameObjects.EyeComponent>(target))
            return false;

        // Sign and emote languages need an unobstructed line of sight, so the
        // message cannot pass through walls.
        if (source is { } sourceEntity)
        {
            var examine = entMan.System<ExamineSystemShared>();
            return examine.InRangeUnOccluded(target, sourceEntity, SignLanguageRange);
        }

        return true;
    }
}

/// <summary>
///     The message is only shown to listeners that understand the language.
///     Everyone else gets nothing at all.
/// </summary>
public sealed partial class UnderstandOnlyCondition : LanguageCondition
{
    public override bool Evaluate(EntityUid target, EntityUid? source, IEntityManager entMan)
    {
        return entMan.System<LanguageSystem>().CanUnderstand(target, Language);
    }
}
