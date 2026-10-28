using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._VanGuard.CCVars;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._VanGuard.UserInterface.RichText;

public sealed partial class EntityMarkupTag : IMarkupTagHandler
{
    [Dependency] private IConfigurationManager _cfg = default!;

    public string Name => "enttex";

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;

        if (!_cfg.GetCVar(VGCCVars.PointingChatSprites))
        {
            control = new Control { Visible = false };
            return true;
        }

        if (!node.Attributes.TryGetValue("id", out var idParam) || !TryParseLong(idParam, out var id))
            return false;

        var size = node.Attributes.TryGetValue("size", out var sizeParam) && TryParseLong(sizeParam, out var s) ? s : 32;
        var scale = node.Attributes.TryGetValue("scale", out var scaleParam) && TryParseLong(scaleParam, out var sc) ? sc : 1;
        var offsetX = node.Attributes.TryGetValue("offsetX", out var xParam) && TryParseLong(xParam, out var x) ? x : 0;
        var offsetY = node.Attributes.TryGetValue("offsetY", out var yParam) && TryParseLong(yParam, out var y) ? y : 0;

        var spriteView = new StaticSpriteView
        {
            SetSize = new Vector2(size * scale, size * scale),
            SpriteScale = new Vector2(scale, scale),
            SpriteOffset = new Vector2((float)offsetX, (float)offsetY),
            OverrideDirection = Direction.South,
        };

        spriteView.SetEntity(new NetEntity((int)id));

        control = spriteView;
        return true;
    }

    private static bool TryParseLong(MarkupParameter param, out long result)
    {
        if (param.TryGetLong(out var l))
        {
            result = l.Value;
            return true;
        }
        if (param.TryGetString(out var s) && long.TryParse(s, out var parsed))
        {
            result = parsed;
            return true;
        }
        result = 0;
        return false;
    }
}
