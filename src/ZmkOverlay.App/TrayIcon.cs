using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ZmkOverlay.App.Interop;
using ZmkOverlay.App.Text;
using ZmkOverlay.Core.Config;

namespace ZmkOverlay.App;

/// <summary>
/// 常駐アプリなので、終了手段が必ず見えている必要がある。
/// オーバーレイは普段クリックを受け取らないため、操作の受け皿はここと設定画面。
/// 受け取る設定に切り替えれば、レイヤーの選択と移動だけはオーバーレイ上でもできる。
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
    private readonly ToolStripMenuItem _mode;
    private readonly ToolStripMenuItem _layersOnly;
    private readonly ToolStripMenuItem _selected;
    private readonly ToolStripMenuItem _always;
    private readonly ToolStripMenuItem _clickable;
    private readonly ToolStripMenuItem _settings;
    private readonly ToolStripMenuItem _setup;
    private readonly ToolStripMenuItem _reload;
    private readonly ToolStripMenuItem _warnings;
    private readonly ToolStripMenuItem _exit;

    private readonly Action<int> _showLayer;
    private readonly Action<string> _setDisplayMode;
    private readonly Action<bool> _setClickThrough;
    private readonly Action _showWarnings;

    private IReadOnlyList<string> _currentWarnings = Array.Empty<string>();
    private bool _enabled = true;

    /// <summary>初期設定を途中で閉じているか。メニューの文言を「続ける」に変える。</summary>
    private bool _setupInProgress;

    /// <summary>直近のバルーンをクリックしたときの動作。バルーンごとに差し替える。</summary>
    private Action? _balloonClicked;

    private bool _suppressModeEvents;
    private bool _suppressClickableEvents;
    private bool _disposed;

    public TrayIcon(
        Action toggle,
        Action<int> showLayer,
        Action openSettings,
        Action openSetup,
        Action reload,
        Action exit,
        string displayMode,
        Action<string> setDisplayMode,
        Action showWarnings,
        bool clickThrough,
        Action<bool> setClickThrough)
    {
        _showLayer = showLayer;
        _setDisplayMode = setDisplayMode;
        _setClickThrough = setClickThrough;
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
        _toggle = new ToolStripMenuItem("", null, (_, _) => toggle());

        // キーボードをまだ書き換えていなくても、ここから各レイヤーを見られる。
        _layers = new ToolStripMenuItem();

        // 並べて、選ばれていない側が何なのかも見えるようにする。
        // チェックボックス 1 個だと、外したときの挙動がどこにも書かれない。
        // 出る場面が少ない順に並べる。どのレイヤーで出すかは設定画面で選ぶ。
        _layersOnly = new ToolStripMenuItem();
        _selected = new ToolStripMenuItem();
        _always = new ToolStripMenuItem();

        _layersOnly.Click += (_, _) => ChooseMode(DisplayModes.LayersOnly);
        _selected.Click += (_, _) => ChooseMode(DisplayModes.SelectedLayers);
        _always.Click += (_, _) => ChooseMode(DisplayModes.Always);

        _mode = new ToolStripMenuItem();
        _mode.DropDownItems.Add(_layersOnly);
        _mode.DropDownItems.Add(_selected);
        _mode.DropDownItems.Add(_always);

        SyncDisplayMode(displayMode);

        // 「クリックスルー」にチェックを付けると、チェックがどちらの状態を指すのか読めない。
        // チェック = オーバーレイを触れる、と読める向きで出す。
        _clickable = new ToolStripMenuItem();
        _clickable.Click += (_, _) => ChooseClickable();

        SyncClickThrough(clickThrough);

        _settings = new ToolStripMenuItem("", null, (_, _) => openSettings());
        _setup = new ToolStripMenuItem("", null, (_, _) => openSetup());
        _reload = new ToolStripMenuItem("", null, (_, _) => reload());

        // バルーンは見逃すと二度と見られないので、警告があるあいだはメニューにも残す。
        _warnings = new ToolStripMenuItem("", null, (_, _) => _showWarnings()) { Visible = false };

        _exit = new ToolStripMenuItem("", null, (_, _) => exit());

        menu.Items.Add(_header);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggle);
        menu.Items.Add(_layers);
        menu.Items.Add(_mode);
        menu.Items.Add(_clickable);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_settings);
        menu.Items.Add(_setup);
        menu.Items.Add(_reload);
        menu.Items.Add(_warnings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exit);

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

        ApplyLanguage();
    }

    /// <summary>
    /// 文言を今の表示言語で入れ直す。設定画面で言語を切り替えたときに呼ぶ。
    /// レイヤーの一覧は <see cref="Configure"/> が作り直す。
    /// </summary>
    public void ApplyLanguage()
    {
        _layers.Text = UiText.ShowLayer;
        _mode.Text = UiText.DisplayMode;
        _layersOnly.Text = UiText.ModeLayersOnly;
        _layersOnly.ToolTipText = UiText.ModeLayersOnlyTip;
        _selected.Text = UiText.ModeSelected;
        _selected.ToolTipText = UiText.ModeSelectedTip;
        _always.Text = UiText.ModeAlways;
        _always.ToolTipText = UiText.ModeAlwaysTip;
        _clickable.Text = UiText.Clickable;
        _clickable.ToolTipText = UiText.ClickableTip;
        _settings.Text = UiText.Settings;
        _setup.Text = _setupInProgress ? UiText.SetupMenuContinue : UiText.SetupMenu;
        _reload.Text = UiText.ReloadKeymap;
        _warnings.Text = UiText.ViewWarnings(_currentWarnings.Count);
        _exit.Text = UiText.Exit;

        ShowState(_enabled, notify: false);
    }

    /// <summary>
    /// キーマップや設定を読み直したときに、メニューの中身を合わせる。
    /// </summary>
    /// <param name="layerShortcut">レイヤー番号 → 手で表示するショートカットの表記。無ければ null。</param>
    /// <param name="clickThroughHotkey">クリックの受け取りを切り替えるショートカットの表記。無ければ null。</param>
    public void Configure(
        string toggleHotkey, IReadOnlyList<(int Id, string Name)> layers, Func<int, string?> layerShortcut,
        bool setupInProgress, string? clickThroughHotkey)
    {
        _toggle.ShortcutKeyDisplayString = toggleHotkey;
        _clickable.ShortcutKeyDisplayString = clickThroughHotkey;

        _setupInProgress = setupInProgress;
        _setup.Text = setupInProgress ? UiText.SetupMenuContinue : UiText.SetupMenu;

        foreach (var old in _layers.DropDownItems.Cast<ToolStripItem>().ToList()) old.Dispose();

        foreach (var (id, name) in layers)
        {
            _layers.DropDownItems.Add(new ToolStripMenuItem($"L{id} {name}".TrimEnd(), null, (_, _) => _showLayer(id))
            {
                ShortcutKeyDisplayString = layerShortcut(id),
            });
        }

        _layers.Enabled = layers.Count > 0;
    }

    private void ChooseMode(string mode)
    {
        if (_suppressModeEvents) return;

        SyncDisplayMode(mode);
        _setDisplayMode(mode);
    }

    private void ChooseClickable()
    {
        if (_suppressClickableEvents) return;

        // ToolStripMenuItem は CheckOnClick ではないので、押しただけでは変わらない。押したあとの向きを自分で決める。
        var clickable = !_clickable.Checked;

        SyncClickThrough(!clickable);
        _setClickThrough(!clickable);
    }

    /// <summary>
    /// 有効・無効をメニュー・ツールチップ・アイコンに反映する。
    ///
    /// 「L1 以上のときだけ表示」などでは、無効にしても画面上は何も変わらない
    /// （もともと出ていない）。切り替わったことが分かる場所が要る。
    /// </summary>
    public void ShowState(bool enabled, bool notify)
    {
        _enabled = enabled;
        var state = enabled ? UiText.StateEnabled : UiText.StateDisabled;

        _toggle.Text = enabled ? UiText.Disable : UiText.Enable;
        _header.Text = $"{BaseTitle} — {state}";
        _icon.Text = $"{BaseTitle} — {state}";
        _icon.Icon = enabled ? _enabledIcon : _disabledIcon;

        if (notify)
            Notify(BaseTitle, enabled ? UiText.OverlayEnabled : UiText.OverlayDisabled);
    }

    /// <summary>設定を読み直したときに、選択状態を実際の設定に合わせ直す。</summary>
    /// <param name="mode"><see cref="DisplayModes"/> のどれか。</param>
    public void SyncDisplayMode(string mode)
    {
        _suppressModeEvents = true;

        _layersOnly.Checked = mode == DisplayModes.LayersOnly;
        _selected.Checked = mode == DisplayModes.SelectedLayers;
        _always.Checked = mode == DisplayModes.Always;

        _suppressModeEvents = false;
    }

    /// <summary>設定を読み直したときに、チェックを実際の設定に合わせ直す。</summary>
    /// <param name="clickThrough">クリックを素通しする設定か。チェックはその裏返し。</param>
    public void SyncClickThrough(bool clickThrough)
    {
        _suppressClickableEvents = true;
        _clickable.Checked = !clickThrough;
        _suppressClickableEvents = false;
    }

    /// <summary>
    /// 警告の一覧を差し替える。前回と中身が変わって 1 件以上あるときだけバルーンでも知らせる。
    /// 設定画面で値を動かすたびに同じ警告が出てくると、うるさいだけになる。
    /// </summary>
    public void ReportWarnings(IReadOnlyList<string> warnings)
    {
        var changed = !warnings.SequenceEqual(_currentWarnings);

        _currentWarnings = warnings;
        _warnings.Visible = warnings.Count > 0;
        _warnings.Text = UiText.ViewWarnings(warnings.Count);

        if (!changed || warnings.Count == 0) return;

        var message = string.Join("\n", warnings.Take(3)) + "\n" + UiText.ClickForAll;

        Notify(UiText.KeymapWarnings(warnings.Count), message, ToolTipIcon.Warning, onClick: _showWarnings);
    }

    public void Notify(string title, string message, ToolTipIcon kind = ToolTipIcon.Info, Action? onClick = null)
    {
        if (message.Length > BalloonTextLimit) message = message[..(BalloonTextLimit - 1)] + "…";

        _balloonClicked = onClick;
        _icon.ShowBalloonTip(5000, title, message, kind);
    }

    private static readonly MethodInfo? ShowContextMenuMethod =
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// NotifyIcon は左クリックでメニューを出す公開 API を持たないので、右クリック時と同じ
    /// 内部メソッドを呼ぶ。ContextMenuStrip.Show を自前で呼ぶと、トレイ用の前面化処理が抜けて
    /// メニューの外をクリックしても閉じなくなる。
    ///
    /// 内部メソッドは .NET の更新で名前が変わりうる。見つからなければ、閉じにくくても出ないよりはよいので自前で出す。
    /// </summary>
    private void ShowMenu()
    {
        if (ShowContextMenuMethod is not null)
        {
            ShowContextMenuMethod.Invoke(_icon, null);
            return;
        }

        _icon.ContextMenuStrip?.Show(System.Windows.Forms.Cursor.Position);
    }

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
