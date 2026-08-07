namespace Content.Shared._VanGuard.Paper;

[RegisterComponent]
public sealed partial class SignatureComponent : Component
{
    [DataField("color")]
    public SignatureInkColor Color = SignatureInkColor.Black;
}

public enum SignatureInkColor
{
    Black,
    Blue,
    LightBlue,
    Red,
    Green,
    Orange,
    Yellow,
    Pink,
    Purple,
    Brown,
    Gray,
    Gold
}

public static class SignatureInkColorExtensions
{
    public static Color ToColor(this SignatureInkColor color)
    {
        return color switch
        {
            SignatureInkColor.Black => Color.FromHex("#161616"),
            SignatureInkColor.Blue => Color.FromHex("#004391"),
            SignatureInkColor.LightBlue => Color.FromHex("#5B97BC"),
            SignatureInkColor.Red => Color.FromHex("#970000"),
            SignatureInkColor.Green => Color.FromHex("#0E7A32"),
            SignatureInkColor.Orange => Color.FromHex("#C97A22"),
            SignatureInkColor.Yellow => Color.FromHex("#C69B17"),
            SignatureInkColor.Pink => Color.FromHex("#D85AA3"),
            SignatureInkColor.Purple => Color.FromHex("#7A43B6"),
            SignatureInkColor.Brown => Color.FromHex("#754D36"),
            SignatureInkColor.Gray => Color.FromHex("#777777"),
            SignatureInkColor.Gold => Color.FromHex("#D4AF37"),
            _ => Color.Black
        };
    }
}
