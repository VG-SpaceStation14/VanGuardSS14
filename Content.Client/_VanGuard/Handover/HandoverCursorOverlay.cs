using System.Numerics;
using Content.Shared._VanGuard.Handover;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Utility;

namespace Content.Client._VanGuard.Handover;

public sealed class HandoverCursorOverlay : Overlay
{
    private readonly IInputManager _input;
    private readonly IEyeManager _eye;
    private readonly IPlayerManager _player;
    private readonly IEntityManager _entMan;
    private readonly Texture _icon;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public HandoverCursorOverlay(IInputManager input, IEyeManager eye, IPlayerManager player, IEntityManager entMan)
    {
        _input = input;
        _eye = eye;
        _player = player;
        _entMan = entMan;
        _icon = entMan.System<SpriteSystem>().Frame0(
            new SpriteSpecifier.Rsi(new ResPath("/Textures/_VanGuard/Interface/Misc/handover_cursor.rsi"), "give_item"));
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player ||
            !_entMan.TryGetComponent<HandoverOfferComponent>(player, out var offer))
            return false;

        return offer.IsTargeting && base.BeforeDraw(in args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var mouse = _input.MouseScreenPosition;
        if (!mouse.IsValid)
            return;

        if (_eye.PixelToMap(mouse).MapId != args.MapId)
            return;

        var scale = Math.Min(1.25f, (args.ViewportControl as Control)?.UIScale ?? 1f);
        var size = _icon.Size * scale;
        // Offset from the cursor so the actual click point remains visible.
        var position = mouse.Position + new Vector2(8f, 8f) * scale;
        args.ScreenHandle.DrawTextureRect(_icon,
            UIBox2.FromDimensions(position, size));
    }
}
