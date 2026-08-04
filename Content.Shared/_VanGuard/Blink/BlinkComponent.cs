using Content.Shared.Humanoid;
using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Blink;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class BlinkComponent : Component
{
    [DataField, AutoNetworkedField]
    public HumanoidVisualLayers EyeLayer = HumanoidVisualLayers.Eyes;

    [DataField] public float NormalMinBlinkInterval = 2.5f;
    [DataField] public float NormalMaxBlinkInterval = 7.0f;
    [DataField] public float NormalBlinkDuration = 0.15f;

    [DataField] public float InjuredMinBlinkInterval = 0.6f;
    [DataField] public float InjuredMaxBlinkInterval = 2.0f;
    [DataField] public float InjuredBlinkDuration = 0.08f;

    [DataField] public float DamageWeight = 1.0f;
    [DataField] public float StaminaWeight = 0.7f;
    [DataField] public float SkipBlinkChance = 0.05f;
    [DataField] public float LongBlinkChance = 0.1f;
    [DataField] public float IntensityLongBlinkBonus = 0.3f;
    [DataField] public float LongBlinkMultiplier = 2.5f;
    [DataField] public float LongIntervalChance = 0.2f;
    [DataField] public float LongIntervalMultiplier = 2f;

    [DataField] public float ReflexBlinkDamageThreshold = 5f;
    [DataField] public float ReflexBlinkDuration = 0.12f;
    [DataField] public float ReflexBlinkChance = 0.9f;

    [AutoNetworkedField]
    public bool EyesClosed;

    public TimeSpan NextBlinkTime;
    public TimeSpan BlinkEndTime;
}