using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Pointing.Components;
using Content.Shared.Database;
using Content.Shared.Pointing;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server.Pointing.EntitySystems;

public sealed partial class PointingSystem : SharedPointingSystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;

    protected override bool ShouldPointingArrowGoRogue()
    {
        return EntityQuery<PointingArrowAngeringComponent>().FirstOrDefault() != null;
    }

    protected override void AfterPoint(
        EntityUid player,
        MapCoordinates mapCoordsPointed,
        EntityUid pointed)
    {
        base.AfterPoint(player, mapCoordsPointed, pointed);

        if (Exists(pointed))
        {
            var effectivePointed = GetPointingTarget(pointed);
            _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(player):user} pointed at {ToPrettyString(effectivePointed):target} {Transform(effectivePointed).Coordinates}");
        }
        else
        {
            _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(player):user} pointed at {GetTileName(mapCoordsPointed)} {GetTileLogPosition(mapCoordsPointed)}");
        }

        // VG-Tweak Start: mirror the point messages into the NanoChat chat feed.
        var viewers = Filter.PvsExcept(player, entityManager: EntityManager).Recipients;
        PlayerManager.TryGetSessionByEntity(player, out var session);
        var (chatTarget, selfMessage, othersMessage, targetMessage) =
            GetPointingMessages(player, pointed, mapCoordsPointed);
        SendChatMessage(session, player, viewers, chatTarget, selfMessage, othersMessage, targetMessage,
            Exists(pointed) ? pointed : null);
        // VG-Tweak End
    }

}
