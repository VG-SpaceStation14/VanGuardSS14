using Content.Shared._VanGuard.NanoChat;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._VanGuard.NanoChat;

/// <summary>
///     A single NanoChat message bubble with a timestamp. Outgoing messages are
///     right-aligned and tinted with the NanoChat accent, incoming ones are
///     left-aligned on a dark panel.
/// </summary>
public sealed partial class NanoChatMessageBubble : BoxContainer
{
    public NanoChatMessageBubble(NanoChatMessage message, bool outgoing)
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical;
        HorizontalExpand = true;
        Margin = new Thickness(0, 1);

        var timeLabel = new Label
        {
            Text = message.Timestamp.ToString("hh\\:mm\\:ss"),
            FontColorOverride = Color.FromHex("#6b7a88"),
            HorizontalAlignment = outgoing ? HAlignment.Right : HAlignment.Left,
            Margin = new Thickness(outgoing ? 0 : 2, 0, outgoing ? 2 : 0, 0),
        };
        timeLabel.AddStyleClass("LabelSubText");
        AddChild(timeLabel);

        var content = message.Content;
        if (message.DeliveryFailed)
            content += $" ({Loc.GetString("nano-chat-delivery-failed")})";

        var panel = new PanelContainer
        {
            HorizontalAlignment = outgoing ? HAlignment.Right : HAlignment.Left,
            MaxWidth = 380f,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = outgoing ? Color.FromHex("#173A4D") : Color.FromHex("#141A21"),
                BorderColor = outgoing ? Color.FromHex("#2A6B85") : Color.FromHex("#2E3B45"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 3,
                ContentMarginBottomOverride = 3,
            },
        };

        var text = new Label
        {
            Text = content,
            FontColorOverride = outgoing ? Color.FromHex("#cfe6ff") : Color.White,
            HorizontalExpand = true,
        };
        panel.AddChild(text);
        AddChild(panel);
    }
}


