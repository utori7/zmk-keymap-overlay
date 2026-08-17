using System.Windows.Media;
using ZmkOverlay.Core.Model;

namespace ZmkOverlay.App.Render;

/// <summary>
/// オーバーレイの配色。半透過の板の上に乗るので、背景側は必ずアルファを持たせる。
/// </summary>
internal static class Theme
{
    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public static readonly Brush Panel = Frozen("#F21A1D23");
    public static readonly Brush PanelBorder = Frozen("#3DFFFFFF");

    public static readonly Brush HeaderText = Frozen("#FFE8EAF0");
    public static readonly Brush MutedText = Frozen("#8CE8EAF0");
    public static readonly Brush FaintText = Frozen("#59E8EAF0");

    public static readonly Brush TabActiveBg = Frozen("#FF3D6EE8");
    public static readonly Brush TabActiveText = Frozen("#FFFFFFFF");
    public static readonly Brush TabIdleBg = Frozen("#1FFFFFFF");
    public static readonly Brush TabIdleText = Frozen("#A6E8EAF0");
    public static readonly Brush TabUnavailableText = Frozen("#59E8EAF0");

    public static readonly Brush KeyBorder = Frozen("#33FFFFFF");
    public static readonly Brush HoldText = Frozen("#B3FFD9A6");

    public static (Brush Background, Brush Foreground) ForKind(KeyKind kind) => kind switch
    {
        KeyKind.Modifier    => (Frozen("#3D4A90E2"), Frozen("#FFD6E6FF")),
        KeyKind.Layer       => (Frozen("#47E8964A"), Frozen("#FFFFE4C4")),
        KeyKind.System      => (Frozen("#47E2555F"), Frozen("#FFFFD2D7")),
        KeyKind.Transparent => (Frozen("#0FFFFFFF"), Frozen("#4DE8EAF0")),
        KeyKind.None        => (Frozen("#00000000"), Frozen("#26E8EAF0")),
        _                   => (Frozen("#26FFFFFF"), Frozen("#FFF0F2F6")),
    };
}
