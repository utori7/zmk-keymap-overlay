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

    public OverlayWindow(AppConfig config, PhysicalLayout layout, Keymap keymap)
    {
        InitializeComponent();

        _config = config;
        _layout = layout;
        _keymap = keymap;

        Opacity = config.Opacity;

        Rebuild();
    }

    /// <summary>キーマップが定義する ZMK レイヤー番号の一覧。</summary>
    public IReadOnlyList<int> LayerIds =>
        _keymap.Layers.Select(l => l.Index).ToList();

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
            Rebuild();

            // 描き直すと寸法が変わるので、出したまま切り替えたときは置き直す。
            if (IsVisible) Reposition();
        }

        return true;
    }

    public void ShowOverlay()
    {
        if (!IsVisible)
        {
            Show();
        }

        // 別の最前面ウィンドウに追い越されることがあるので、表示のたびに主張し直す。
        Topmost = false;
        Topmost = true;

        Reposition();
    }

    public void HideOverlay() => Hide();

    public void Toggle()
    {
        if (IsVisible) HideOverlay();
        else ShowOverlay();
    }

    private void Rebuild()
    {
        Body.Content = KeymapRenderer.BuildPanel(_config, _layout, _keymap, _slot);
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

        var x = work.Left + (work.Width - rect.Width) / 2;
        var y = _config.Position switch
        {
            "TopCenter" => work.Top + margin,
            "Center" => work.Top + (work.Height - rect.Height) / 2,
            _ => work.Bottom - rect.Height - margin,
        };

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
