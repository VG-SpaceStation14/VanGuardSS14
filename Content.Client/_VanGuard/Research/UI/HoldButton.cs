using System;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._VanGuard.Research.UI;

/// <summary>
/// A button that does not fire on a simple click: the mouse button must be
/// held for <see cref="HoldDuration"/> seconds, shown as a yellow fill, to
/// confirm. Used for the "research whole branch" action to prevent accidental
/// spending of a large amount of research points. The hold survives info
/// panel rebuilds via <see cref="ResumeHold"/>.
/// </summary>
public sealed class HoldButton : Button
{
    /// <summary>How long (seconds) the mouse button must be held to confirm.</summary>
    public const float HoldDuration = 1.25f;

    /// <summary>Fired when the mouse button is pressed down on the button.</summary>
    public event Action? HoldStarted;

    /// <summary>Fired when the mouse button is released before the hold completes.</summary>
    public event Action? HoldCancelled;

    /// <summary>Fired once when the hold is completed.</summary>
    public event Action? Confirmed;

    private bool _holding;
    private float _remaining = HoldDuration;
    private float _delayRemaining;

    public HoldButton()
    {
        OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick || Disabled)
                return;

            _holding = true;
            _remaining = HoldDuration;
            _delayRemaining = ResearchTechnologyNode.FillDelay;
            HoldStarted?.Invoke();
        };

        OnKeyBindUp += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            // Only cancel a hold that is actually in progress: KeyBindUp may fire
            // for a release of a click that started while the button was disabled,
            // or after the hold already completed and confirmed.
            if (!_holding)
                return;

            _holding = false;
            _remaining = HoldDuration;
            HoldCancelled?.Invoke();
        };
    }

    /// <summary>
    /// Continues a hold on a rebuilt button (the info panel is rebuilt whenever
    /// the server pushes a new console state while the player holds).
    /// </summary>
    public void ResumeHold(float remaining)
    {
        _holding = true;
        _delayRemaining = MathF.Max(0f, remaining - HoldDuration);
        _remaining = Math.Clamp(remaining, 0f, HoldDuration);
    }

    /// <summary>Current hold progress from 0 to 1, for the fill visual.</summary>
    private float Progress =>
        _delayRemaining > 0f ? 0f : Math.Clamp(1f - _remaining / HoldDuration, 0f, 1f);

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_holding)
            return;

        // Small grace period so a simple click never shows the confirmation fill.
        if (_delayRemaining > 0f)
        {
            _delayRemaining = MathF.Max(0f, _delayRemaining - args.DeltaSeconds);
            return;
        }

        _remaining -= args.DeltaSeconds;
        if (_remaining <= 0f)
        {
            _holding = false;
            _remaining = HoldDuration;
            _delayRemaining = 0f;
            Confirmed?.Invoke();
        }
    }

    /// <summary>
    /// Draws the yellow confirmation fill from the left edge to the right,
    /// using the same yellow as the chain-research fills on the technology
    /// cards. The fill reuses the button's own (chamfered) stylebox so its
    /// bevel matches the button exactly and never pokes out past it. The
    /// button is tinted navy via ModulateSelf, so the drawing modulate is
    /// temporarily reset to keep the fill the pure chain-research yellow.
    /// </summary>
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var progress = Progress;
        if (progress <= 0f)
            return;

        var fillWidth = PixelWidth * progress;
        if (fillWidth <= 0f)
            return;

        // Replicate ContainerButton's stylebox lookup.
        var style = StyleBoxOverride;
        if (style == null && TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var box))
            style = box;

        var oldMod = handle.Modulate;
        handle.Modulate = Color.White;

        try
        {
            if (style is StyleBoxTexture texture)
            {
                var tinted = new StyleBoxTexture(texture)
                {
                    Modulate = ResearchTechnologyNode.ChainFillColor,
                };

                // Keep the fill at least as wide as the corner patches so the
                // 9-slice always draws the chamfered corners correctly - even
                // during the very first frames of the hold.
                var minWidth = (texture.PatchMarginLeft + texture.PatchMarginRight)
                               * texture.TextureScale.X * UIScale;
                var width = MathF.Max(fillWidth, minWidth);
                tinted.Draw(handle, UIBox2.FromDimensions(0, 0, width, PixelHeight), UIScale);
            }
            else
            {
                handle.DrawRect(UIBox2.FromDimensions(0, 0, fillWidth, PixelHeight),
                    ResearchTechnologyNode.ChainFillColor);
            }
        }
        finally
        {
            handle.Modulate = oldMod;
        }
    }
}