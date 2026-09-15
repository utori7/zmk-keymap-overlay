using System.Windows;
using System.Windows.Media;
using ZmkOverlay.Core.Model;

namespace ZmkOverlay.App.Render;

/// <summary>
/// オーバーレイの配色。半透過の板の上に乗るので、背景側は必ずアルファを持たせる。
///
/// タイピング中に視界の端に出るものなので、彩度は抑えてある。
/// キーの種類の違いは、色味をわずかに付けた背景と文字色だけで表す。
/// </summary>
internal static class Theme
{
    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>日本語のラベルは Segoe UI に無いので、同系統の Yu Gothic UI で補う。</summary>
    public static readonly FontFamily Font = new("Segoe UI, Yu Gothic UI");

    /// <summary>文字幅の実測に使う。描画と同じ書体でないと測った値が合わない。</summary>
    public static readonly Typeface Typeface =
        new(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static readonly Brush Panel = Frozen("#EB15171C");
    public static readonly Brush PanelBorder = Frozen("#1FFFFFFF");

    public static readonly Brush TabActiveBg = Frozen("#FF3F6BC9");
    public static readonly Brush TabActiveText = Frozen("#FFFFFFFF");
    public static readonly Brush TabIdleText = Frozen("#99E6E8EE");
    public static readonly Brush TabUnavailableText = Frozen("#4DE6E8EE");
    public static readonly Brush TabUnavailableOutline = Frozen("#38E6E8EE");

    public static readonly Brush HintText = Frozen("#66E6E8EE");

    public static readonly Brush ComboBg = Frozen("#12FFFFFF");
    public static readonly Brush ComboKeysText = Frozen("#80E6E8EE");
    public static readonly Brush ComboLabelText = Frozen("#D9E6E8EE");

    public static readonly Brush HoldText = Frozen("#8CE6E8EE");
    public static readonly Brush TransparentOutline = Frozen("#1CFFFFFF");

    // キーごとに作り直さないよう、種類ごとに 1 組だけ持つ。
    private static readonly (Brush, Brush) NormalKey = (Frozen("#1FFFFFFF"), Frozen("#FFF3F4F7"));
    private static readonly (Brush, Brush) ModifierKey = (Frozen("#294F7BD9"), Frozen("#FFC4D6F5"));
    private static readonly (Brush, Brush) LayerKey = (Frozen("#29D9A05B"), Frozen("#FFF0D9BC"));
    private static readonly (Brush, Brush) SystemKey = (Frozen("#29D95B6A"), Frozen("#FFF2C7CD"));
    private static readonly (Brush, Brush) NoneKey = (Frozen("#00000000"), Frozen("#26E6E8EE"));

    public static (Brush Background, Brush Foreground) ForKind(KeyKind kind) => kind switch
    {
        KeyKind.Modifier => ModifierKey,
        KeyKind.Layer => LayerKey,
        KeyKind.System => SystemKey,
        KeyKind.None => NoneKey,
        _ => NormalKey,
    };
}
