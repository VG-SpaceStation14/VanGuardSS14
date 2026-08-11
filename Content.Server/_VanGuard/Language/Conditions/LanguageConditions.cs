using Content.Shared._VanGuard.Language;
using Content.Shared.ActionBlocker;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Content.Shared.Speech.Muting;

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
        if (entMan.HasComponent<BlockListeningComponent>(target))
            return false;

        if (entMan.HasComponent<MutedStatusEffectComponent>(target))
            return false;

        return true;
    }
}

/// <summary>
///     The listener must be able to see the speaker. Used by sign/emote languages.
/// </summary>
public sealed partial class ListenerVisionCondition : LanguageCondition
{
    public override bool Evaluate(EntityUid target, EntityUid? source, IEntityManager entMan)
    {
        // Entities without eyes cannot follow sign languages.
        return entMan.HasComponent<Robust.Shared.GameObjects.EyeComponent>(target);
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
