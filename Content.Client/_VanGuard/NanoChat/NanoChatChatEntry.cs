using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared._VanGuard.NanoChat;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._VanGuard.NanoChat;

/// <summary>
///     A single conversation in the NanoChat contact list: contact name, job
///     title, unread indicator and an active-chat highlight.
/// </summary>
public sealed partial class NanoChatChatEntry : Button
{
    public static readonly Color SelectedColor = Color.FromHex("#173247");

    private readonly PanelContainer _unreadIndicator;
    private readonly Label _nameLabel;
    private readonly Label _jobLabel;

    public NanoChatChatEntry(NanoChatRecipient recipient, uint number)
    {
        HorizontalExpand = true;
        AddStyleClass(StyleClass.ButtonOpenBoth);
        Margin = new Thickness(0, 1);

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        _unreadIndicator = new PanelContainer
        {
            MinSize = new Vector2(8, 8),
            MaxSize = new Vector2(8, 8),
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(4, 0, 6, 0),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#4ad9ff"),
                BorderColor = Color.FromHex("#2a7fa0"),
            },
            Visible = false,
        };
        box.AddChild(_unreadIndicator);

        var textBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };

        _nameLabel = new Label
        {
            Text = recipient.Name,
            HorizontalExpand = true,
            ClipText = true,
        };
        textBox.AddChild(_nameLabel);

        _jobLabel = new Label
        {
            Text = recipient.JobTitle ?? string.Empty,
            HorizontalExpand = true,
            ClipText = true,
            FontColorOverride = Color.Gray,
            Visible = !string.IsNullOrEmpty(recipient.JobTitle),
        };
        textBox.AddChild(_jobLabel);

        box.AddChild(textBox);

        box.AddChild(new Label
        {
            Text = $"#{number:D4}",
            FontColorOverride = Color.FromHex("#7a8a99"),
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
        });

        AddChild(box);
    }

    public void SetUnread(bool unread)
    {
        _unreadIndicator.Visible = unread;
    }

    public void SetSelected(bool selected)
    {
        ModulateSelfOverride = selected ? SelectedColor : null;
    }
}

