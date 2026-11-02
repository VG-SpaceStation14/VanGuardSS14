using Robust.Shared.Map;

namespace Content.Server._VanGuard.Weather;

[RegisterComponent]
public sealed partial class LavalandWeatherSchedulerComponent : Component
{
    [DataField]
    public List<LavalandWeatherStage> Stages = new()
    {
        new() { DurationMin = 360, DurationMax = 540 },
        new() { Weather = "WeatherAshfallLight", DurationMin = 30, DurationMax = 30, Message = "lavaland-ash-warning" },
        new() { Weather = "WeatherAshfall", DurationMin = 60, DurationMax = 120, Message = "lavaland-ash-storm" },
        new() { Weather = "WeatherAshfallLight", DurationMin = 30, DurationMax = 30, Message = "lavaland-ash-clearing" },
    };

    [DataField]
    public int Stage;

    [DataField]
    public TimeSpan NextUpdate;
}

[DataDefinition]
public sealed partial class LavalandWeatherStage
{
    [DataField]
    public string? Weather;

    [DataField]
    public float DurationMin = 30;

    [DataField]
    public float DurationMax = 60;

    [DataField]
    public string? Message;
}
