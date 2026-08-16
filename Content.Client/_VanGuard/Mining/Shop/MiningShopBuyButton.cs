using System;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._VanGuard.Mining.Shop;

/// <summary>
/// Invisible click/hover catcher for <see cref="MiningShopItemCard"/>.
/// The visible square is a separate panel drawn behind it; <see cref="OnDrawModeChanged"/>
/// lets the card repaint that panel on hover / press / disabled state changes.
/// </summary>
public sealed class MiningShopBuyButton : Button
{
    public event Action? OnDrawModeChanged;

    public MiningShopBuyButton()
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
