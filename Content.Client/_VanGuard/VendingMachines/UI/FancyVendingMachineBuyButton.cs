using System;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._VanGuard.VendingMachines.UI;

/// <summary>
///     Invisible click/hover catcher for <see cref="FancyVendingMachineItemCard"/>.
///     The visible square is a separate panel drawn behind it (the ADT "FancyButton"
///     pattern); <see cref="OnDrawModeChanged"/> lets the card repaint that panel on
///     hover / press / disabled state changes.
/// </summary>
public sealed class FancyVendingMachineBuyButton : Button
{
    public event Action? OnDrawModeChanged;

    public FancyVendingMachineBuyButton()
    {
        StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.Transparent,
            BorderColor = Color.Transparent,
        };
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        OnDrawModeChanged?.Invoke();
    }
}

