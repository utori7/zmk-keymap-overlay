using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZmkOverlay.App.Render;

/// <summary>
/// タブ 1 枚。クリックされたときに、どのレイヤーなのかを返せるようにするためだけの型。
///
/// <see cref="FrameworkElement.Tag"/> に番号を入れる手もあるが、型で見分けられるほうが
/// 当たった要素から親を辿るときに確実で、ホバーの状態を置く場所にもなる。
/// Grid は装飾もテンプレートも持たないので、派生させても見た目と寸法は変わらない。
/// </summary>
internal sealed class LayerTab : Grid
{
    /// <summary>ZMK のレイヤー番号。リスト上の位置ではない。</summary>
    public int LayerId { get; init; }

    /// <summary>色を塗る側。ホバーではこの背景だけを差し替える。</summary>
    public Border? Fill { get; init; }

    /// <summary>触れていないときの背景。</summary>
    public Brush? IdleBackground { get; init; }

    /// <summary>触れているときの背景。クリックを受け取らない間は null。</summary>
    public Brush? HoverBackground { get; init; }

    /// <summary>
    /// ホバーの色を消す。板が消えたり中身が差し替わったりすると
    /// <see cref="UIElement.MouseLeave"/> が来ないことがあり、光ったまま残る。
    /// </summary>
    public void ClearHover()
    {
        if (Fill is not null) Fill.Background = IdleBackground;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);

        if (Fill is not null && HoverBackground is not null) Fill.Background = HoverBackground;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ClearHover();
    }
}
