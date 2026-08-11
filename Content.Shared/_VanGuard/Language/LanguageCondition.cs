using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Language;

/// <summary>
///     A server-only condition that gates a language's transmission or reception.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class LanguageCondition
{
    /// <summary>
    ///     The language this condition applies to. Injected by the prototype on read.
    /// </summary>
    public ProtoId<LanguagePrototype> Language;

    /// <summary>
    ///     If true the condition is checked against the listener, otherwise against the speaker.
    /// </summary>
    [DataField]
    public bool CheckListener;

    /// <summary>
    ///     Evaluates the condition for the given entity.
    /// </summary>
    public abstract bool Evaluate(EntityUid target, EntityUid? source, IEntityManager entMan);
}
