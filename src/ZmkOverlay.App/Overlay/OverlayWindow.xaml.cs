using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ZmkOverlay.App.Interop;
using ZmkOverlay.App.Render;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;

namespace ZmkOverlay.App.Overlay;

/// <summary>
/// キーマップを描く半透過オーバーレイ。
///
/// 普段はクリックを受け取らない。WS_EX_TRANSPARENT を立てることで、
/// クリックは下のアプリへ素通しする。<see cref="AppConfig.ClickThrough"/> を外すと、
/// このフラグだけを下ろしてクリックとドラッグを受け取るようになる
/// （タブでレイヤーを選ぶ・板を動かす）。
///
/// WS_EX_NOACTIVATE と WS_EX_TOOLWINDOW は、受け取るかどうかに関わらず常に立てたまま。
/// タイピング中に前面へ出す道具なので、クリックできるようになってもフォーカスを奪わないことが最優先。
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

    /// <summary>クリックとドラッグを受け取る状態か。<see cref="AppConfig.ClickThrough"/> の裏返し。</summary>
    private bool _interactive;

    /// <summary>ドラッグ中、カーソルを見に行く間隔（ミリ秒）。</summary>
    private const int DragPollMs = 15;

    private DispatcherTimer? _dragTimer;

    /// <summary>押した瞬間のカーソル位置（画面座標・実ピクセル）。</summary>
    private NativeMethods.POINT _dragOrigin;

    /// <summary>押した瞬間のウィンドウの左上（実ピクセル）。</summary>
    private int _dragWindowLeft;
    private int _dragWindowTop;

    /// <summary>押した場所にあったタブ。離すまでにドラッグへ変わらなければ、これのクリックとして扱う。</summary>
    private LayerTab? _pressedTab;

    /// <summary>押してから、ドラッグと言える距離だけ動いたか。</summary>
    private bool _dragged;

    public OverlayWindow(AppConfig config, PhysicalLayout layout, Keymap keymap)
    {
        InitializeComponent();

        _config = config;
        _layout = layout;
        _keymap = keymap;
        _interactive = config.IsInteractive;

        Opacity = config.Opacity;

        Rebuild();
    }

    /// <summary>タブをクリックした（ZMK のレイヤー番号）。</summary>
    public event Action<int>? LayerTabClicked;

    /// <summary>ドラッグで動かし終えた。設定の基準位置からのずれ（DIP）。</summary>
    public event Action<double, double>? Dragged;

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

        // ウィンドウは起動時に作られるが、ここは最初に表示するまで走らない。
        // それまでに決まった状態を、いちばん初めにまとめて反映する。
        ApplyWindowStyle();
    }

    /// <summary>
    /// 拡張スタイルを、いまの状態に合わせる。
    ///
    /// WS_EX_TRANSPARENT だけが出し入れの対象。WS_EX_NOACTIVATE（フォーカスを奪わない）と
    /// WS_EX_TOOLWINDOW（Alt+Tab に出さない）は常に立てる。
    /// WPF が AllowsTransparency のために立てた WS_EX_LAYERED を落とさないよう、必ず読んでから書く。
    /// </summary>
    private void ApplyWindowStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE)
            | NativeMethods.WS_EX_NOACTIVATE
            | NativeMethods.WS_EX_TOOLWINDOW;

        style = _interactive
            ? style & ~NativeMethods.WS_EX_TRANSPARENT
            : style | NativeMethods.WS_EX_TRANSPARENT;

        // SWP_FRAMECHANGED は付けない。WS_EX_TRANSPARENT は当たり判定にしか効かず、
        // 半透過ウィンドウにフレーム変更を投げると描き直しのちらつきを招く。
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, style);
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
        CancelDrag();

        _config = config;
        _layout = layout;
        _keymap = keymap;
        _interactive = config.IsInteractive;

        if (_slot >= _keymap.Layers.Count) _slot = 0;

        Opacity = config.Opacity;
        ApplyWindowStyle();
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
            // 差し替える前に消す。載せたまま中身が変わると MouseLeave が来ず、光ったまま残る。
            ClearTabHover();

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
        ClearTabHover();
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

        // 消えるなら、掴んでいる途中も押している途中も無かったことにする。
        CancelDrag();
        ClearTabHover();

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
        _panels = KeymapRenderer.BuildPanels(_config, _layout, _keymap, _interactive);
        Body.Content = _panels[_slot];
        UpdateLayout();
    }

    // ---- クリックとドラッグ ----

    /// <summary>
    /// 押されたら、ドラッグかクリックかの判定を始める。
    ///
    /// マウスの捕捉（CaptureMouse）は使わない。最前面でないウィンドウの捕捉は Windows の仕様上あてにならず、
    /// 離したことを取りこぼすと掴んだまま戻らなくなる。合図キーの解放と同じく、自分で見に行く
    /// （<see cref="Interop.LayerSyncService"/>）。
    /// </summary>
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);

        if (!_interactive || _dragTimer is not null) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!NativeMethods.GetCursorPos(out var cursor)) return;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return;

        _dragOrigin = cursor;
        _dragWindowLeft = rect.Left;
        _dragWindowTop = rect.Top;
        _dragged = false;
        _pressedTab = TabAt(e.OriginalSource as DependencyObject);

        _dragTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(DragPollMs),
        };

        _dragTimer.Tick += OnDragTick;
        _dragTimer.Start();

        e.Handled = true;
    }

    private void OnDragTick(object? sender, EventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            CancelDrag();
            return;
        }

        var dx = cursor.X - _dragOrigin.X;
        var dy = cursor.Y - _dragOrigin.Y;

        // 少し動いただけならクリックのまま。OS の「ドラッグと見なす距離」に合わせる。
        if (!_dragged)
        {
            var scale = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            _dragged = Math.Abs(dx) >= SystemParameters.MinimumHorizontalDragDistance * scale
                    || Math.Abs(dy) >= SystemParameters.MinimumVerticalDragDistance * scale;
        }

        if (_dragged) MoveTo(_dragWindowLeft + dx, _dragWindowTop + dy);

        // 動かしてから離したかを見る。先に見ると、最後のひと動きが反映されない。
        if (!NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON)) EndDrag();
    }

    /// <summary>離した。動かしていればずれを知らせ、動かしていなければタブのクリックとして扱う。</summary>
    private void EndDrag()
    {
        var tab = _pressedTab;
        var dragged = _dragged;

        CancelDrag();

        if (dragged)
        {
            if (CurrentOffset() is { } offset) Dragged?.Invoke(offset.X, offset.Y);
            return;
        }

        if (tab is not null) LayerTabClicked?.Invoke(tab.LayerId);
    }

    /// <summary>掴んでいる途中の状態を捨てる。知らせは出さない。</summary>
    private void CancelDrag()
    {
        _pressedTab = null;
        _dragged = false;

        if (_dragTimer is null) return;

        _dragTimer.Stop();
        _dragTimer.Tick -= OnDragTick;
        _dragTimer = null;
    }

    /// <summary>当たった要素から親を辿って、タブを探す。</summary>
    private static LayerTab? TabAt(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is LayerTab tab) return tab;

            // TextBlock の中の Run のように、ビジュアルツリーに居ない要素から始まることがある。
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }

    /// <summary>
    /// マウスを載せた色を消す。板が消えたり中身が差し替わったりすると MouseLeave が来ないことがあり、
    /// 次に出したときに、カーソルの無いタブが光ったままになる。
    /// </summary>
    private void ClearTabHover()
    {
        if (_slot >= _panels.Count) return;

        foreach (var tab in TabsIn(_panels[_slot])) tab.ClearHover();
    }

    private static IEnumerable<LayerTab> TabsIn(DependencyObject root)
    {
        if (root is LayerTab tab)
        {
            yield return tab;
            yield break;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
            foreach (var found in TabsIn(VisualTreeHelper.GetChild(root, i)))
                yield return found;
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
        var (x, y) = AnchorPosition(work, rect, dpiScale);

        // ドラッグで動かした分。置いた場所そのものではなく基準からのずれで持つので、
        // カーソルのあるモニタに出すという動きを保ったまま、同じような場所に出せる。
        x += (int)Math.Round(_config.OffsetX * dpiScale);
        y += (int)Math.Round(_config.OffsetY * dpiScale);

        // ずれを足すと画面の外へも出せてしまうので、作業領域に収める。
        // 板が画面より大きいときは、左上が切れないほうを優先する。
        x = Math.Max(work.Left, Math.Min(x, work.Right - rect.Width));
        y = Math.Max(work.Top, Math.Min(y, work.Bottom - rect.Height));

        MoveTo(x, y);
    }

    /// <summary>
    /// 設定の <see cref="AppConfig.Position"/> と <see cref="AppConfig.Margin"/> だけで決まる位置。
    /// ドラッグのずれは含まない。置き直しと、ずれの計算の両方がここを使うので、
    /// 「置いた場所」と「次に出る場所」が食い違わない。
    /// </summary>
    private (int X, int Y) AnchorPosition(NativeMethods.RECT work, NativeMethods.RECT rect, double dpiScale)
    {
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

        return (x, y);
    }

    /// <summary>いまの位置が、基準の位置からどれだけずれているか（DIP）。位置が取れなければ null。</summary>
    private (double X, double Y)? CurrentOffset()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return null;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return null;

        var work = GetTargetWorkArea(hwnd);
        var dpiScale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var (anchorX, anchorY) = AnchorPosition(work, rect, dpiScale);

        return ((rect.Left - anchorX) / dpiScale, (rect.Top - anchorY) / dpiScale);
    }

    private void MoveTo(int x, int y)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

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
