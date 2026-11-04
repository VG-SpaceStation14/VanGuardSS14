using System.Diagnostics.CodeAnalysis;
using Content.Shared.StatusIcon;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._VanGuard.UserInterface.RichText;

/// <summary>
///     Renders a job icon inline in rich text, for example in radio messages.
/// </summary>
/// <remarks>
///     The tag takes the prototype id of the icon as its value: <c>[jobicon="JobIconSecurity"/]</c>.
///     If the requested icon cannot be loaded, a generic fallback icon is used instead.
/// </remarks>
public sealed partial class JobIconMarkupTag : IMarkupTagHandler
{
    private const int IconSize = 16;
    private const int IconTopPadding = 1;
    private const int IconRightPadding = 4;

    private static readonly ProtoId<JobIconPrototype> PrimaryFallbackIcon = "JobIconNoId";
    private static readonly ProtoId<JobIconPrototype> SecondaryFallbackIcon = "JobIconUnknown";

    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IEntitySystemManager _entitySystems = default!;

    private SpriteSystem? _spriteSystem;

    public string Name => "jobicon";

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;

        if (node.Closing)
            return false;

        if (!node.Value.TryGetString(out var requestedId) || string.IsNullOrWhiteSpace(requestedId))
            return false;

        _spriteSystem ??= _entitySystems.GetEntitySystem<SpriteSystem>();

        if (TryCreateIconControl(requestedId, out control))
            return true;

        if (requestedId != PrimaryFallbackIcon.Id && TryCreateIconControl(PrimaryFallbackIcon.Id, out control))
            return true;

        if (requestedId != SecondaryFallbackIcon.Id && TryCreateIconControl(SecondaryFallbackIcon.Id, out control))
            return true;

        return false;
    }

    private bool TryCreateIconControl(string iconId, [NotNullWhen(true)] out Control? control)
    {
        control = null;

        if (!_prototype.TryIndex<JobIconPrototype>(iconId, out var prototype))
            return false;

        Texture texture;
        try
        {
            texture = _spriteSystem!.Frame0(prototype.Icon);
        }
        catch (Exception)
        {
            return false;
        }

        var icon = new TextureRect
        {
            Texture = texture,
            SetWidth = IconSize,
            SetHeight = IconSize,
            Stretch = TextureRect.StretchMode.Scale,
            VerticalAlignment = Control.VAlignment.Center,
            MouseFilter = Control.MouseFilterMode.Stop,
            Margin = new Thickness(0, IconTopPadding, IconRightPadding, 0),
        };

        if (!string.IsNullOrWhiteSpace(prototype.JobName))
            icon.ToolTip = prototype.LocalizedJobName;

        control = icon;
        return true;
    }
}
