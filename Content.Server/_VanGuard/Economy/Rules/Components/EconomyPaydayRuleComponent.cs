using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._VanGuard.Economy.Rules.Components;

/// <summary>
///     Game rule that fires the payroll every <see cref="Interval"/>.
/// </summary>
[RegisterComponent]
public sealed partial class EconomyPaydayRuleComponent : Component
{
    [DataField]
    public TimeSpan Interval = TimeSpan.FromMinutes(10);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextPayday;
}
