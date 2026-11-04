using Content.Server.Chat.Managers;
using Content.Shared.Chat;
using Content.Shared.Weather;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Weather;

public sealed partial class LavalandWeatherSchedulerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedWeatherSystem _weather = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = AllEntityQuery<LavalandWeatherSchedulerComponent, MapComponent>();
        while (query.MoveNext(out var mapUid, out var scheduler, out var map))
        {
            if (now < scheduler.NextUpdate)
                continue;

            if (scheduler.Stages.Count == 0)
                continue;

            if (scheduler.Stage >= scheduler.Stages.Count)
                scheduler.Stage = 0;

            var stage = scheduler.Stages[scheduler.Stage++];
            var duration = _random.NextFloat(stage.DurationMin, stage.DurationMax);
            scheduler.NextUpdate = now + TimeSpan.FromSeconds(duration);

            if (stage.Weather is { } weather)
                _weather.TrySetWeather(map.MapId, weather, out _, TimeSpan.FromSeconds(duration));

            if (stage.Message is { } message)
            {
                var text = Loc.GetString(message);
                _chat.ChatMessageToManyFiltered(
                    Filter.BroadcastMap(map.MapId),
                    ChatChannel.Local,
                    text,
                    text,
                    mapUid,
                    false,
                    true,
                    null);
            }
        }
    }
}
