using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ZmkOverlay.App.Interop;
using ZmkOverlay.App.Render;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;

namespace ZmkOverlay.App.Overlay;

/// <summary>
/// キーマップを描く半透過オーバーレイ。
///
/// このウィンドウは入力を一切受け取らない。SourceInitialized で
/// WS_EX_TRANSPARENT / WS_EX_NOACTIVATE / WS_EX_TOOLWINDOW を立てることで、
/// クリックは下のアプリに素通しし、フォーカスも奪わない。
/// タイピング中に前面へ出す道具なので、フォーカスを奪わないことが最優先。
/// </summary>
public partial class OverlayWindow : Window
{
    private AppConfig _config;
    private PhysicalLayout _layout;
    private Keymap _keymap;

    /// <summary>表示中のレイヤーの、リスト上の位置。ZMK のレイヤー番号とは別物。</summary>
    private int _slot;

    /// <summary>全レイヤーぶんの作り置き。寸法は揃えてある。</summary>
    private IReadOnlyList<FrameworkElement> _panels = Array.Empty<FrameworkElement>();

    /// <summary>隠す前に待つ描画の回数（<see cref="HideOverlay"/>）。</summary>
    private const int FramesBeforeHide = 3;

    /// <summary>隠すまでに待つ残りの描画回数。0 なら隠す途中ではない。</summary>
    private int _framesUntilHide;

    public OverlayWindow(AppConfig config, PhysicalLayout layout, Keymap keymap)
    {
        InitializeComponent();

        _config = config;
        _layout = layout;
        _keymap = keymap;

        Opacity = config.Opacity;

        Rebuild();
    }

    public Keymap Keymap => _keymap;

    /// <summary>キーマップが定義する ZMK レイヤー番号の一覧。</summary>
    public IReadOnlyList<int> LayerIds =>
        _keymap.Layers.Select(l => l.Index).ToList();

    /// <summary>レイヤー番号と表示名。トレイのメニューに並べる。</summary>
    public IReadOnlyList<(int Id, string Name)> Layers =>
        _keymap.Layers.Select(l => (l.Index, l.Name)).ToList();

    /// <summary>いま表示している ZMK レイヤー番号。</summary>
    public int CurrentLayerId => _keymap.Layers[_slot].Index;

    /// <summary>合図キーを持つレイヤーの一覧（レイヤー番号 → キー名）。</summary>
    public IReadOnlyList<(int LayerId, string SignalKey)> SignalKeys =>
        _keymap.Layers
            .Where(l => !string.IsNullOrWhiteSpace(l.SignalKey))
            .Select(l => (l.Index, l.SignalKey!))
            .ToList();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            style
            | NativeMethods.WS_EX_TRANSPARENT
            | NativeMethods.WS_EX_NOACTIVATE
            | NativeMethods.WS_EX_TOOLWINDOW);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // DPI が変わるとレイアウトの実ピクセル寸法が変わるので、置き直す。
        Dispatcher.BeginInvoke(new Action(Reposition),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>設定とデータを差し替えて描き直す。トレイの「再読み込み」用。</summary>
    public void Reload(AppConfig config, PhysicalLayout layout, Keymap keymap)
    {
        _config = config;
        _layout = layout;
        _keymap = keymap;

        if (_slot >= _keymap.Layers.Count) _slot = 0;

        Opacity = config.Opacity;
        Rebuild();

        if (IsVisible) Reposition();
    }

    /// <summary>
    /// 表示するレイヤーを切り替える。表示状態は変えない。
    /// 無いレイヤー番号を渡されたら false を返して何もしない。
    /// </summary>
    public bool TrySetLayer(int layerId)
    {
        var slot = _keymap.Layers.FindIndex(l => l.Index == layerId);
        if (slot < 0) return false;

        if (slot != _slot)
        {
            _slot = slot;

            // パネルは全レイヤーで同じ寸法に揃えてあるので、差し替えるだけでよい。
            // 大きさが変わらないので置き直しも要らず、表示中に切り替えても板が動かない。
            Body.Content = _panels[slot];
        }

        return true;
    }

    /// <summary>表示中か。隠す途中（中身は消えていて、ウィンドウを隠すのを待っている）なら false。</summary>
    public bool IsShown => IsVisible && _framesUntilHide == 0;

    public void ShowOverlay()
    {
        CancelPendingHide();
        Body.Visibility = Visibility.Visible;

        if (!IsVisible)
        {
            Show();
        }

        // 別の最前面ウィンドウに追い越されることがあるので、表示のたびに主張し直す。
        Topmost = false;
        Topmost = true;

        Reposition();
    }

    /// <summary>
    /// 中身を消した絵を描かせてから、ウィンドウを隠す。
    ///
    /// 透過ウィンドウ（AllowsTransparency）は、隠しても最後に描いた絵を持っている。
    /// 隠しているあいだは描き直さないので、そのまま隠すと、次に表示したとき新しいレイヤーが描けるまでの一瞬、
    /// 前に出していたレイヤーが見える（L1 を離して L2 を押すと、L1 が一瞬出てから L2 になる）。
    /// 先に空の絵を描かせておけば、その一瞬は何も見えない。表示する側を遅らせずに済む。
    ///
    /// 中身は次の描画で消えるので、見た目は今までどおりすぐ消える。
    /// </summary>
    public void HideOverlay()
    {
        if (!IsVisible || _framesUntilHide > 0) return;

        // Hidden は場所を取ったまま見えなくするので、ウィンドウの大きさは変わらない。
        Body.Visibility = Visibility.Hidden;

        // 空の絵を描く描画と、それが画面に届くまでの余裕を 1 回ずつ待つ。
        _framesUntilHide = FramesBeforeHide;
        CompositionTarget.Rendering += OnRenderingBeforeHide;
    }

    private void OnRenderingBeforeHide(object? sender, EventArgs e)
    {
        if (--_framesUntilHide > 0) return;

        CompositionTarget.Rendering -= OnRenderingBeforeHide;
        Hide();
    }

    /// <summary>隠す途中で表示を求められたら、隠すのをやめる。</summary>
    private void CancelPendingHide()
    {
        if (_framesUntilHide == 0) return;

        CompositionTarget.Rendering -= OnRenderingBeforeHide;
        _framesUntilHide = 0;
    }

    public void Toggle()
    {
        if (IsShown) HideOverlay();
        else ShowOverlay();
    }

    private void Rebuild()
    {
        _panels = KeymapRenderer.BuildPanels(_config, _layout, _keymap);
        Body.Content = _panels[_slot];
        UpdateLayout();
    }

    /// <summary>
    /// カーソルのあるモニタの作業領域内に配置する。
    ///
    /// 位置決めは WPF の Left/Top ではなく SetWindowPos で物理ピクセルのまま行う。
    /// DPI の異なるモニタをまたぐと WPF 座標系と実ピクセルの対応が崩れるため、
    /// レイアウト済みの実寸（GetWindowRect）を使って動かすだけにするのが確実。
    /// </summary>
    private void Reposition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return;

        var work = GetTargetWorkArea(hwnd);

        var dpiScale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var margin = (int)Math.Round(_config.Margin * dpiScale);

        // "BottomCenter" / "TopLeft" のように、縦位置 + 横位置で書く。"Center" だけは画面中央。
        var position = _config.Position ?? "";
        const StringComparison ignoreCase = StringComparison.OrdinalIgnoreCase;

        var x = position.EndsWith("Left", ignoreCase) ? work.Left + margin
              : position.EndsWith("Right", ignoreCase) ? work.Right - rect.Width - margin
              : work.Left + (work.Width - rect.Width) / 2;

        var y = position.StartsWith("Top", ignoreCase) ? work.Top + margin
              : position.Equals("Center", ignoreCase) ? work.Top + (work.Height - rect.Height) / 2
              : work.Bottom - rect.Height - margin;

        // 画面より大きい場合でも左上が切れないようにする。
        x = Math.Max(work.Left, x);
        y = Math.Max(work.Top, y);

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private static NativeMethods.RECT GetTargetWorkArea(IntPtr hwnd)
    {
        var monitor = NativeMethods.GetCursorPos(out var cursor)
            ? NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST)
            : NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);

        var info = new NativeMethods.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };

        if (NativeMethods.GetMonitorInfo(monitor, ref info)) return info.rcWork;

        return new NativeMethods.RECT
        {
            Left = 0,
            Top = 0,
            Right = (int)SystemParameters.PrimaryScreenWidth,
            Bottom = (int)SystemParameters.PrimaryScreenHeight,
        };
    }
}
