using System;
using System.Drawing;
using System.Windows.Forms;
using ZmkOverlay.App.Text;

namespace ZmkOverlay.App;

/// <summary>
/// 常駐アプリなので、終了手段が必ず見えている必要がある。
/// オーバーレイ自体はクリックを受け取らないため、操作の受け皿はここだけ。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string BaseTitle = "ZMK Keymap Overlay";

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggle;
    private readonly ToolStripMenuItem _layersOnly;
    private readonly ToolStripMenuItem _always;
    private readonly ToolStripMenuItem _runAtLogin;

    private readonly Action<bool> _setAlwaysVisible;
    private bool _suppressModeEvents;
    private bool _disposed;

    public TrayIcon(
        Action toggle,
        Action reload,
        Action exit,
        bool alwaysVisible,
        Action<bool> setAlwaysVisible,
        bool runAtLogin,
        Action<bool> setRunAtLogin)
    {
        _setAlwaysVisible = setAlwaysVisible;

        // 押すと何が起きるかを書く。「有効 / 無効」だと、いまどちらなのか、
        // 押したらどうなるのかが読み取れない。
        _toggle = new ToolStripMenuItem(UiText.Disable, null, (_, _) => toggle());

        // 2 つ並べて、選ばれていない側が何なのかも見えるようにする。
        // チェックボックス 1 個だと、外したときの挙動がどこにも書かれない。
        _layersOnly = new ToolStripMenuItem(UiText.ModeLayersOnly)
        {
            Checked = !alwaysVisible,
            ToolTipText = UiText.ModeLayersOnlyTip,
        };

        _always = new ToolStripMenuItem(UiText.ModeAlways)
        {
            Checked = alwaysVisible,
            ToolTipText = UiText.ModeAlwaysTip,
        };

        _layersOnly.Click += (_, _) => ChooseMode(always: false);
        _always.Click += (_, _) => ChooseMode(always: true);

        var mode = new ToolStripMenuItem(UiText.DisplayMode);
        mode.DropDownItems.Add(_layersOnly);
        mode.DropDownItems.Add(_always);

        _runAtLogin = new ToolStripMenuItem(UiText.RunAtLogin)
        {
            CheckOnClick = true,
            Checked = runAtLogin,
            ToolTipText = UiText.RunAtLoginTip,
        };

        // 失敗して元に戻すときにも CheckedChanged が飛ぶので、往復しないよう抑制する。
        _runAtLogin.CheckedChanged += (_, _) =>
        {
            if (_suppressModeEvents) return;
            setRunAtLogin(_runAtLogin.Checked);
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(mode);
        menu.Items.Add(_runAtLogin);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(UiText.ReloadAll, null, (_, _) => reload());
        menu.Items.Add(UiText.Exit, null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            // Phase 0 は専用アイコンを持たない。実行できることを優先する。
            Icon = SystemIcons.Application,
            Text = BaseTitle,
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icon.DoubleClick += (_, _) => toggle();
    }

    private void ChooseMode(bool always)
    {
        if (_suppressModeEvents) return;

        SyncAlwaysVisible(always);
        _setAlwaysVisible(always);
    }

    /// <summary>
    /// 有効・無効をメニューとツールチップに反映する。
    ///
    /// 「L1 以上のときだけ表示」では、無効にしても画面上は何も変わらない
    /// （もともと出ていない）。切り替わったことが分かる場所が要る。
    /// </summary>
    public void ShowState(bool enabled, bool notify)
    {
        _toggle.Text = enabled ? UiText.Disable : UiText.Enable;
        _icon.Text = $"{BaseTitle} — {(enabled ? UiText.StateEnabled : UiText.StateDisabled)}";

        if (notify)
            Notify(BaseTitle, enabled ? UiText.OverlayEnabled : UiText.OverlayDisabled);
    }

    /// <summary>登録に失敗したときなど、実際の状態に戻す。</summary>
    public void SyncRunAtLogin(bool value)
    {
        if (_runAtLogin.Checked == value) return;

        _suppressModeEvents = true;
        _runAtLogin.Checked = value;
        _suppressModeEvents = false;
    }

    /// <summary>設定を読み直したときに、選択状態を実際の設定に合わせ直す。</summary>
    public void SyncAlwaysVisible(bool always)
    {
        _suppressModeEvents = true;

        _always.Checked = always;
        _layersOnly.Checked = !always;

        _suppressModeEvents = false;
    }

    public void Notify(string title, string message, ToolTipIcon kind = ToolTipIcon.Info)
        => _icon.ShowBalloonTip(5000, title, message, kind);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _icon.Visible = false;
        _icon.Dispose();
    }
}
