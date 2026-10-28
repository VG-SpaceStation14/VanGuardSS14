using System.Collections.Generic;
using Content.Server.Chat.Managers;
using Content.Shared.Chat;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using Robust.Shared.Maths;
using Robust.Shared.IoC;

namespace Content.Server.Pointing.EntitySystems;

internal sealed partial class PointingSystem
{
    [Dependency] private IChatManager _chatManager = default!;

    private static readonly Color PointingChatColor = Color.FromHex("#b8b0c7");

    private void SendChatMessage(
        ICommonSession? session,
        EntityUid source,
        IEnumerable<ICommonSession> viewers,
        EntityUid pointed,
        string selfMessage,
        string viewerMessage,
        string? viewerPointedAtMessage = null,
        EntityUid? iconTarget = null)
    {
        if (session != null)
        {
            var selfChatMessage = viewerMessage;
            var wrappedSelf = BuildPointingChatMessage(selfChatMessage, iconTarget);
            _chatManager.ChatMessageToOne(
                ChatChannel.Local,
                selfChatMessage,
                wrappedSelf,
                EntityUid.Invalid,
                false,
                session.Channel,
                PointingChatColor);
        }

        foreach (var viewer in viewers)
        {
            if (viewer == session) 
                continue;

            var viewerEntity = viewer.AttachedEntity;
            var chatMessage = viewerEntity == pointed && viewerPointedAtMessage != null
                ? viewerPointedAtMessage
                : viewerMessage;
            var wrappedChat = BuildPointingChatMessage(chatMessage, iconTarget);

            _chatManager.ChatMessageToOne(
                ChatChannel.Local,
                chatMessage,
                wrappedChat,
                EntityUid.Invalid,
                false,
                viewer.Channel,
                PointingChatColor);
        }
    }

    private string BuildPointingChatMessage(string message, EntityUid? iconTarget)
    {
        var escapedMessage = FormattedMessage.EscapeText(message);

        if (iconTarget == null || !Exists(iconTarget.Value))
            return escapedMessage;

        return $"{escapedMessage} [enttex id=\"{(int) GetNetEntity(iconTarget.Value)}\" size=\"32\" scale=\"1\" offsetX=\"1\" offsetY=\"0\"]";
    }
}
