using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ZmkOverlay.App.Interop;
using ZmkOverlay.App.Text;

namespace ZmkOverlay.App;

/// <summary>
/// 常駐アプリなので、終了手段が必ず見えている必要がある。
/// オーバーレイ自体はクリックを受け取らないため、操作の受け皿はここだけ。
///
/// GlobalUsings で Color / Point / Size / Rectangle などは WPF 側に寄せてあるので、
/// このファイルで System.Drawing のそれらを使うときは完全修飾で書くこと。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string BaseTitle = "ZMK Keymap Overlay";

    /// <summary>バルーンの本文はこれより長いと切り捨てられるか、表示されない。</summary>
    private const int BalloonTextLimit = 250;

    private readonly NotifyIcon _icon;
    private readonly Icon _enabledIcon;
    private readonly Icon _disabledIcon;

    private readonly ToolStripLabel _header;
    private readonly ToolStripMenuItem _toggle;
    private readonly ToolStripMenuItem _layers;
    private readonly ToolStripMenuItem _layersOnly;
    private readonly ToolStripMenuItem _always;
    private readonly ToolStripMenuItem _runAtLogin;
    private readonly ToolStripMenuItem _warnings;

    private readonly Action<int> _showLayer;
    private readonly Action<bool> _setAlwaysVisible;
    private readonly Action<IReadOnlyList<string>> _showWarnings;

    private IReadOnlyList<string> _currentWarnings = Array.Empty<string>();

    /// <summary>直近のバルーンをクリックしたときの動作。バルーンごとに差し替える。</summary>
    private Action? _balloonClicked;

    private bool _suppressModeEvents;
    private bool _disposed;

    public TrayIcon(
        Action toggle,
        Action<int> showLayer,
        Action openSettings,
        Action reload,
        Action exit,
        bool alwaysVisible,
        Action<bool> setAlwaysVisible,
        bool runAtLogin,
        Action<bool> setRunAtLogin,
        Action<IReadOnlyList<string>> showWarnings)
    {
        _showLayer = showLayer;
        _setAlwaysVisible = setAlwaysVisible;
        _showWarnings = showWarnings;

        _enabledIcon = LoadAppIcon();
        _disabledIcon = MakeDimmed(_enabledIcon);

        var menu = new ContextMenuStrip();

        // 見出しに状態を出す。アイコンにマウスを載せてツールチップを待たなくても読めるように。
        _header = new ToolStripLabel(BaseTitle)
        {
            Font = new Font(menu.Font, FontStyle.Bold),
        };

        // 押すと何が起きるかを書く。「有効 / 無効」だと、いまどちらなのか、
        // 押したらどうなるのかが読み取れない。
        _toggle = new ToolStripMenuItem(UiText.Disable, null, (_, _) => toggle());

        // キーボードをまだ書き換えていなくても、ここから各レイヤーを見られる。
        _layers = new ToolStripMenuItem(UiText.ShowLayer);

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

        // バルーンは見逃すと二度と見られないので、警告があるあいだはメニューにも残す。
        _warnings = new ToolStripMenuItem("", null, (_, _) => _showWarnings(_currentWarnings))
        {
            Visible = false,
        };

        menu.Items.Add(_header);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggle);
        menu.Items.Add(_layers);
        menu.Items.Add(mode);
        menu.Items.Add(_runAtLogin);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(UiText.Settings, null, (_, _) => openSettings());
        menu.Items.Add(UiText.ReloadKeymap, null, (_, _) => reload());
        menu.Items.Add(_warnings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(UiText.Exit, null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = _enabledIcon,
            Text = BaseTitle,
            Visible = true,
            ContextMenuStrip = menu,
        };

        // 右クリックだけだと、初めての人がアイコンを左クリックして何も起きずに戸惑う。
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowMenu();
        };

        _icon.BalloonTipClicked += (_, _) => _balloonClicked?.Invoke();
    }

    /// <summary>
    /// キーマップや設定を読み直したときに、メニューの中身を合わせる。
    /// </summary>
    public void Configure(string toggleHotkey, IReadOnlyList<(int Id, string Name)> layers, bool manualLayerKeys)
    {
        _toggle.ShortcutKeyDisplayString = toggleHotkey;

        foreach (var old in _layers.DropDownItems.Cast<ToolStripItem>().ToList()) old.Dispose();

        foreach (var (id, name) in layers)
        {
            var item = new ToolStripMenuItem($"L{id} {name}".TrimEnd(), null, (_, _) => _showLayer(id));

            if (manualLayerKeys && id is >= 0 and <= 9)
                item.ShortcutKeyDisplayString = $"Ctrl+Alt+{id}";

            _layers.DropDownItems.Add(item);
        }

        _layers.Enabled = layers.Count > 0;
    }

    private void ChooseMode(bool always)
    {
        if (_suppressModeEvents) return;

        SyncAlwaysVisible(always);
        _setAlwaysVisible(always);
    }

    /// <summary>
    /// 有効・無効をメニュー・ツールチップ・アイコンに反映する。
    ///
    /// 「L1 以上のときだけ表示」では、無効にしても画面上は何も変わらない
    /// （もともと出ていない）。切り替わったことが分かる場所が要る。
    /// </summary>
    public void ShowState(bool enabled, bool notify)
    {
        var state = enabled ? UiText.StateEnabled : UiText.StateDisabled;

        _toggle.Text = enabled ? UiText.Disable : UiText.Enable;
        _header.Text = $"{BaseTitle} — {state}";
        _icon.Text = $"{BaseTitle} — {state}";
        _icon.Icon = enabled ? _enabledIcon : _disabledIcon;

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

    /// <summary>警告の一覧を差し替える。1 件以上あればバルーンでも知らせる。</summary>
    public void ReportWarnings(IReadOnlyList<string> warnings)
    {
        _currentWarnings = warnings;
        _warnings.Visible = warnings.Count > 0;
        _warnings.Text = UiText.ViewWarnings(warnings.Count);

        if (warnings.Count == 0) return;

        var message = string.Join("\n", warnings.Take(3)) + "\n" + UiText.ClickForAll;

        Notify(UiText.KeymapWarnings(warnings.Count), message, ToolTipIcon.Warning,
            onClick: () => _showWarnings(_currentWarnings));
    }

    public void Notify(string title, string message, ToolTipIcon kind = ToolTipIcon.Info, Action? onClick = null)
    {
        if (message.Length > BalloonTextLimit) message = message[..(BalloonTextLimit - 1)] + "…";

        _balloonClicked = onClick;
        _icon.ShowBalloonTip(5000, title, message, kind);
    }

    /// <summary>
    /// NotifyIcon は左クリックでメニューを出す公開 API を持たないので、右クリック時と同じ
    /// 内部メソッドを呼ぶ。ContextMenuStrip.Show を自前で呼ぶと、トレイ用の前面化処理が抜けて
    /// メニューの外をクリックしても閉じなくなる。
    /// </summary>
    private void ShowMenu() =>
        typeof(NotifyIcon)
            .GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(_icon, null);

    /// <summary>exe に埋め込んだアイコンを、トレイの大きさ（DPI で変わる）に合う面で読む。</summary>
    private static Icon LoadAppIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("ZmkOverlay.app.ico");

        return stream is null
            ? (Icon)SystemIcons.Application.Clone()
            : new Icon(stream, SystemInformation.SmallIconSize);
    }

    /// <summary>無効のときのアイコン。色を抜いて薄くし、状態がトレイで一目で分かるようにする。</summary>
    private static Icon MakeDimmed(Icon source)
    {
        using var original = source.ToBitmap();
        using var dimmed = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);

        var grayscale = new ColorMatrix(new[]
        {
            new[] { 0.30f, 0.30f, 0.30f, 0f, 0f },
            new[] { 0.59f, 0.59f, 0.59f, 0f, 0f },
            new[] { 0.11f, 0.11f, 0.11f, 0f, 0f },
            new[] { 0f, 0f, 0f, 0.55f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f },
        });

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(grayscale);

        using (var graphics = Graphics.FromImage(dimmed))
        {
            graphics.DrawImage(original,
                new System.Drawing.Rectangle(0, 0, original.Width, original.Height),
                0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
        }

        var handle = dimmed.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _icon.Visible = false;
        _icon.Dispose();

        _enabledIcon.Dispose();
        _disabledIcon.Dispose();
    }
}
