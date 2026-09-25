using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Windows.Threading;
using ZmkOverlay.Core.Zmk;
using ZmkOverlay.App.Render;
using ZmkOverlay.App.Text;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Text;

namespace ZmkOverlay.App.Settings;

/// <summary>設定画面のページ。左の一覧と同じ順に並べること（<see cref="SettingsWindow.ShowPage"/> は番号で引く）。</summary>
public enum SettingsPage
{
    Display,
    Layers,
    Keyboard,
    General,
}

/// <summary>
/// 設定画面。config.json を手で編集しなくて済むようにする。
///
/// 変更は即時反映して自動保存する（OK / キャンセルは無い）。値を動かすたびに
/// いまの設定を複製して書き換え、<see cref="ISettingsHost.TryApply"/> に渡す。
/// 読めない設定になったら反映も保存もされないので、画面の値を元に戻して理由を出す。
///
/// オーバーレイと違って普通のウィンドウで、フォーカスも取る。
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly string[] Positions =
        { "BottomCenter", "BottomLeft", "BottomRight", "TopCenter", "TopLeft", "TopRight", "Center" };

    private static readonly string[] Languages = { "auto", "ja", "en" };

    private static readonly string[] SignalKeyChoices =
        Enumerable.Range(13, 12).Select(n => $"F{n}").ToArray();

    private readonly ISettingsHost _host;
    private readonly FrameworkElement[] _pages;

    /// <summary>スライダーは動かしている最中に毎回反映すると重いので、止まってから反映する。</summary>
    private readonly DispatcherTimer _debounce;
    private Action? _pending;

    /// <summary>このウィンドウを開いてから合図を受け取ったレイヤー。</summary>
    private readonly HashSet<int> _received = new();
    private readonly Dictionary<int, TextBlock> _signalMarks = new();

    /// <summary>ショートカットの記録。初期設定の案内と同じ処理を使う。</summary>
    private readonly HotkeyCapture _capture;

    /// <summary>レイヤーの表の中のショートカット入力欄。表を作り直すときに捨てる。</summary>
    private readonly List<TextBox> _layerShortcutBoxes = new();

    private GitHubSource _gitHub => _host.GitHub;

    /// <summary>「取得」で一覧を得たリポジトリ。キーマップが複数あって選んでもらうあいだ覚えておく。</summary>
    private GitHubLocation? _gitHubLocation;
    private GitHubTree? _gitHubTree;

    private UiLanguage? _textsLanguage;

    /// <summary>InitializeComponent 中にも値変更イベントが飛ぶので、準備が終わるまで無視する。</summary>
    private bool _ready;

    /// <summary>画面に値を流し込んでいる最中。このあいだの変更イベントは利用者の操作ではない。</summary>
    private bool _loading;

    internal SettingsWindow(ISettingsHost host)
    {
        InitializeComponent();

        _host = host;
        _pages = new FrameworkElement[] { DisplayPage, LayersPage, KeyboardPage, GeneralPage };

        // Esc やキーの確定でフォーカスを渡す先。反映より先に抜けて、ホットキーの一時解除を終わらせる。
        _capture = new HotkeyCapture(_host, Apply, () => Nav.Focus());
        _capture.Attach(HotkeyBox, HotkeyTarget.Toggle);
        _capture.Attach(ClickThroughHotkeyBox, HotkeyTarget.ClickThrough);

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            var pending = _pending;
            _pending = null;
            pending?.Invoke();
        };

        _host.Applied += OnApplied;
        _host.SignalReceived += OnSignalReceived;

        // 別のアプリに切り替えたときにショートカットを外したままにしない。
        Deactivated += (_, _) => _capture.End();

        Closed += (_, _) =>
        {
            _host.Applied -= OnApplied;
            _host.SignalReceived -= OnSignalReceived;
            _debounce.Stop();
            _capture.End();
        };

        VersionText.Text = ProjectLinks.AppVersion;

        // 拡大率の高いノートで作業領域に収まらないと、下端に出す失敗の理由が画面の外に出る。
        var work = SystemParameters.WorkArea;
        MinHeight = Math.Min(MinHeight, work.Height);
        MinWidth = Math.Min(MinWidth, work.Width);
        Height = Math.Min(Height, work.Height);
        Width = Math.Min(Width, work.Width);

        _ready = true;

        ApplyTexts();
        Refresh();
        Nav.SelectedIndex = 0;
    }

    public void ShowPage(SettingsPage page) => Nav.SelectedIndex = (int)page;

    // ---- 反映 ----

    /// <summary>複製を書き換えて反映を試す。失敗したら画面を今の設定に戻して理由を出す。</summary>
    private void Apply(Action<AppConfig> change)
    {
        if (!_ready || _loading) return;

        var next = _host.Config.Clone();
        change(next);

        if (_host.TryApply(next, out var error))
        {
            ShowError(null);
            return;
        }

        ShowError(error);
        Refresh();
    }

    private void ApplyLater(Action<AppConfig> change)
    {
        if (!_ready || _loading) return;

        _pending = () => Apply(change);
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>
    /// 反映の完了はこの画面の操作の途中（コンボボックスの選択変更の中など）で届くことがある。
    /// その場で項目を作り直すと操作中のコントロールを壊すので、一段遅らせる。
    /// </summary>
    private void OnApplied() =>
        Dispatcher.BeginInvoke(() =>
        {
            ApplyTexts();
            Refresh();
        });

    private void ShowError(string? message)
    {
        ErrorText.Text = message ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- 画面への流し込み ----

    private void Refresh()
    {
        _loading = true;

        try
        {
            var config = _host.Config;
            var fromZmk = config.Zmk.IsEnabled;

            // キーボード
            SampleNote.Visibility = fromZmk ? Visibility.Collapsed : Visibility.Visible;

            var github = config.Zmk.IsGitHub ? config.Zmk.GitHub : null;
            GitHubRefresh.Visibility = github is not null ? Visibility.Visible : Visibility.Collapsed;
            if (github is not null)
            {
                GitHubStatus.Text = UiText.GitHubInUse(github.Repository, github.Branch, github.KeymapPath ?? "");
                if (string.IsNullOrWhiteSpace(GitHubUrl.Text)) GitHubUrl.Text = $"https://github.com/{github.Repository}";
            }
            KeymapPath.Text = fromZmk ? Resolve(config.Zmk.KeymapFile!) : UiText.SampleKeymap;
            KeymapPath.ToolTip = KeymapPath.Text;

            // 指定が無いときは、どこで見つけたか（または推定したか）を見せる。
            // 推定だと分からないまま使うと、配置の違いに気づけない。
            var hasLayoutFile = !string.IsNullOrWhiteSpace(config.Zmk.PhysicalLayoutFile);
            LayoutPath.Text = SourceActions.LayoutDescription(_host);
            LayoutPath.ToolTip = LayoutPath.Text;
            LayoutBrowse.IsEnabled = fromZmk;
            LayoutAuto.IsEnabled = fromZmk && hasLayoutFile;

            // 自分の zmk-config にキーの並びがあるなら、ZMK 本体から取る必要はない。
            ZmkLayoutPanel.Visibility = fromZmk && _host.LayoutSource is not (LayoutSource.FoundNearby or LayoutSource.Keymap)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(ZmkShieldBox.Text))
                ZmkShieldBox.Text = ZmkShieldSync.SuggestShield(config, _host.ConfigPath) ?? "";

            var us = config.KeyboardLayout.Equals("us", StringComparison.OrdinalIgnoreCase);
            HostJis.IsChecked = !us;
            HostUs.IsChecked = us;

            var warnings = _host.Warnings;
            WarningsBox.Text = string.Join(Environment.NewLine, warnings);
            WarningsBox.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoWarnings.Visibility = warnings.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

            // 表示
            var mode = config.EffectiveDisplayMode;
            ModeLayersOnlyRadio.IsChecked = mode == DisplayModes.LayersOnly;
            ModeSelectedRadio.IsChecked = mode == DisplayModes.SelectedLayers;
            ModeAlwaysRadio.IsChecked = mode == DisplayModes.Always;

            ShownLayersList.Visibility = config.IsSelectedLayers ? Visibility.Visible : Visibility.Collapsed;
            BuildShownLayers();

            // スライダーは範囲で丸めるので、読み取りもスライダーの値に合わせる。
            // 設定ファイルを手で書き換えて範囲の外になっているときは、その旨を下に出す（RefreshRangeNote）。
            SizeSlider.Value = config.KeyUnitPx;
            SizeValue.Text = $"{SizeSlider.Value:0}px";
            OpacitySlider.Value = Math.Round(config.Opacity * 100);
            OpacityValue.Text = $"{OpacitySlider.Value:0}%";
            MarginSlider.Value = config.Margin;
            MarginValue.Text = $"{MarginSlider.Value:0}px";

            var position = Array.FindIndex(Positions, p => p.Equals(config.Position, StringComparison.OrdinalIgnoreCase));
            PositionBox.SelectedIndex = Math.Max(0, position);

            // 「画面の中央」では余白を使わない。動かせるままにすると、効かない値を触らせることになる。
            var atCenter = config.Position.Equals("Center", StringComparison.OrdinalIgnoreCase);
            MarginSlider.IsEnabled = !atCenter;
            MarginValue.Opacity = atCenter ? 0.4 : 1.0;
            MarginCenterNote.Visibility = atCenter ? Visibility.Visible : Visibility.Collapsed;

            ClickableBox.IsChecked = config.IsInteractive;

            // ドラッグで動かしたあとだけ、戻す入口を出す。同じ位置を選び直しても戻せないため。
            var dragged = config.HasOffset ? Visibility.Visible : Visibility.Collapsed;
            ResetOffsetNote.Visibility = dragged;
            ResetOffsetButton.Visibility = dragged;

            RefreshStatus();
            RefreshRangeNote(config);
            RefreshPreview();

            // レイヤー
            SyncEnabledBox.IsChecked = config.LayerSync.Enabled;
            ManualKeysBox.IsChecked = config.EnableManualLayerKeys;
            SyncHoldRadio.IsChecked = config.LayerSync.IsHoldMode;
            SyncToggleRadio.IsChecked = !config.LayerSync.IsHoldMode;
            SyncHoldRadio.IsEnabled = SyncToggleRadio.IsEnabled = config.LayerSync.Enabled;

            LayerTableNote.Text = fromZmk ? UiText.LayerTableNote : UiText.SampleCannotChange;
            BuildLayerTable(namesEditable: fromZmk, signalsEditable: fromZmk && config.LayerSync.Enabled);

            // ショートカットの欄。記録の途中なら、「押してください」や衝突の理由を上書きしない。
            _capture.RefreshText(skipFocused: true);

            // 全般
            var language = Array.FindIndex(Languages, l => l.Equals(config.Language, StringComparison.OrdinalIgnoreCase));
            LanguageBox.SelectedIndex = Math.Max(0, language);
            RunAtLoginBox.IsChecked = _host.RunAtLogin;
            ConfigPathText.Text = _host.ConfigPath;
            ConfigPathText.ToolTip = _host.ConfigPath;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// いまオーバーレイがどう振る舞うかを 1 行で出し、そのままでは出ようがないときは直し方も出す。
    ///
    /// 出ない理由は独立に複数ある（無効・見せ方・表示するレイヤーの選択・追従の有無・合図キーの有無）。
    /// どれも「設定が正しくても出ない」ことがあるので、3 ページを見比べずに済むようにここへ集める。
    /// </summary>
    private void RefreshStatus()
    {
        var config = _host.Config;
        var layers = _host.Keymap.Layers;

        var state = _host.OverlayEnabled ? UiText.StateEnabled : UiText.StateDisabled;

        var mode = config.EffectiveDisplayMode switch
        {
            DisplayModes.Always => UiText.ModeAlways,
            DisplayModes.SelectedLayers => UiText.ModeSelected,
            _ => UiText.ModeLayersOnly,
        };

        string follow;
        if (!config.LayerSync.Enabled)
        {
            follow = UiText.StatusNotFollowing;
        }
        else
        {
            var auto = _host.Keymap.AutoShownLayerIds();
            var followed = layers.Select(l => l.Index).Where(auto.Contains).ToList();

            follow = followed.Count > 0
                ? UiText.StatusFollowing(UiText.LayerList(followed))
                : UiText.StatusFollowingNothing;
        }

        StatusText.Text = UiText.StatusLine(state, mode, follow);

        // 出ようがない組み合わせ。上から順に、いちばん手前の原因だけを出す。
        var baseId = layers.Count > 0 ? layers[0].Index : 0;
        var showsAtRest = config.ShowsLayer(baseId, baseId);
        var allHidden = config.IsSelectedLayers && layers.Count > 0
            && layers.All(l => config.HiddenLayers.Contains(l.Index));

        var why = !_host.OverlayEnabled ? UiText.StatusWhyDisabled(config.ToggleHotkey.ToString())
            : allHidden ? UiText.StatusWhyNoShownLayers
            : !showsAtRest && !config.LayerSync.Enabled && !config.EnableManualLayerKeys ? UiText.StatusWhyNoTrigger
            : null;

        StatusWhy.Text = why ?? "";
        StatusWhy.Visibility = why is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 設定ファイルの値が、この画面で選べる範囲の外にあるとき。スライダーは端で止まるので、
    /// 何も言わないと「動かしていないのに値が変わった」ように見える。
    /// </summary>
    private void RefreshRangeNote(AppConfig config)
    {
        var outside = new List<string>();

        void Check(Slider slider, string label, double value)
        {
            if (value < slider.Minimum || value > slider.Maximum)
                outside.Add($"{label}: {UiText.ValueOutOfRange(value)}");
        }

        Check(SizeSlider, UiText.SizeLabel, config.KeyUnitPx);
        Check(OpacitySlider, UiText.OpacityLabel, Math.Round(config.Opacity * 100));
        Check(MarginSlider, UiText.MarginLabel, config.Margin);

        RangeNote.Text = string.Join(Environment.NewLine, outside);
        RangeNote.Visibility = outside.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// オーバーレイと同じ部品で描く。入りきらなければ縮めるが、入るなら実際の大きさで見せる。
    /// </summary>
    private void RefreshPreview()
    {
        var config = _host.Config;
        if (_host.Keymap.Layers.Count == 0) return;

        var panel = KeymapRenderer.BuildPanels(config, _host.Layout, _host.Keymap)[0];
        panel.Opacity = config.Opacity;
        PreviewContent.Content = panel;
    }

    /// <summary>
    /// 「選んだレイヤーのときだけ表示」のチェック。チェックを付ける＝表示する。
    /// 設定ファイルには表示しない側（<see cref="AppConfig.HiddenLayers"/>）を持つ。
    /// </summary>
    private void BuildShownLayers()
    {
        ShownLayersList.Children.Clear();

        var config = _host.Config;
        var layers = _host.Keymap.Layers;
        if (layers.Count == 0) return;

        var baseId = layers[0].Index;
        var autoShown = _host.Keymap.AutoShownLayerIds();

        foreach (var layer in layers)
        {
            var id = layer.Index;

            // L0 に付けると何も押していないときにも出る、というのは名前だけでは分からない。
            // 合図キーの無いレイヤーは、付けてもキーボードの操作では出ない。
            // 追従を切っているときはどれも出ないので、全部に添えても読みにくくなるだけ（オーバーレイのタブの点線と同じ扱い）。
            var suffix = id == baseId ? UiText.BaseLayerSuffix
                : config.LayerSync.Enabled && !autoShown.Contains(id) ? UiText.NoSignalSuffix
                : null;

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = $"L{id} {layer.Name}".TrimEnd() });
            if (suffix is not null)
                content.Children.Add(new TextBlock { Text = suffix, Opacity = 0.65 });

            var box = new CheckBox
            {
                Content = content,
                IsChecked = !config.HiddenLayers.Contains(id),
                Margin = new Thickness(0, 2, 16, 4),
            };

            box.Click += (_, _) =>
            {
                var show = box.IsChecked == true;

                Apply(c =>
                {
                    c.HiddenLayers.RemoveAll(hidden => hidden == id);
                    if (!show) c.HiddenLayers.Add(id);
                    c.HiddenLayers.Sort();
                });
            };

            ShownLayersList.Children.Add(box);
        }
    }

    /// <summary>
    /// レイヤーごとの名前・合図キー・テスト・手で出すショートカットの表。
    /// レイヤーごとの設定はすべてこの 1 行に集める。列の幅は XAML の見出しと揃える。
    /// </summary>
    private void BuildLayerTable(bool namesEditable, bool signalsEditable)
    {
        // 作り直す前に、前の入力欄を記録の対象から外す。残すとイベントが宛先を失う。
        foreach (var box in _layerShortcutBoxes) _capture.Detach(box);
        _layerShortcutBoxes.Clear();

        LayerTable.Children.Clear();
        _signalMarks.Clear();

        var layers = _host.Keymap.Layers;

        for (var slot = 0; slot < layers.Count; slot++)
        {
            var layer = layers[slot];
            var id = layer.Index;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            row.Children.Add(new TextBlock { Text = $"L{id}", VerticalAlignment = VerticalAlignment.Center });

            // 手で出すショートカットは、合図キーを持たないレイヤー（L0 を含む）でも使える。
            // 先に入れておき、合図キーを持たない行でも欄が抜けないようにする。
            AddShortcutCell(row, id);

            var name = new TextBox
            {
                Text = layer.Name,
                Width = 170,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                IsEnabled = namesEditable,
            };

            AutomationProperties.SetName(name, $"L{id} {UiText.ColumnName}");

            name.LostFocus += (_, _) => CommitLayerName(id, name.Text);
            name.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) CommitLayerName(id, name.Text);
            };

            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            // ベースレイヤーは合図キーを持たない（何も押していない状態そのもの）。
            if (slot == 0)
            {
                var none = new TextBlock
                {
                    Text = "—",
                    Opacity = 0.4,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = UiText.BaseHasNoSignal,
                };

                Grid.SetColumn(none, 2);
                row.Children.Add(none);
                LayerTable.Children.Add(row);
                continue;
            }

            var combo = new ComboBox { Width = 110, HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = signalsEditable };
            combo.Items.Add(UiText.NoSignal);
            foreach (var key in SignalKeyChoices) combo.Items.Add(key);

            var selected = Array.FindIndex(SignalKeyChoices, k => k.Equals(layer.SignalKey, StringComparison.OrdinalIgnoreCase));
            combo.SelectedIndex = selected + 1;

            AutomationProperties.SetName(combo, $"L{id} {UiText.ColumnSignal}");

            if (layer.DetectedSignalKey is { } detectedKey)
                combo.ToolTip = UiText.DetectedFromKeymap(detectedKey);

            var detected = layer.DetectedSignalKey;

            combo.SelectionChanged += (_, _) => Apply(config =>
            {
                var key = id.ToString(CultureInfo.InvariantCulture);
                var chosen = combo.SelectedIndex <= 0 ? null : SignalKeyChoices[combo.SelectedIndex - 1];

                // キーマップから読み取れた値と同じなら上書きを持たない。
                // 持ってしまうと、あとでファームを書き換えたときに追従しなくなる。
                if (string.Equals(chosen, detected, StringComparison.OrdinalIgnoreCase))
                    config.Zmk.SignalKeys.Remove(key);
                else
                    config.Zmk.SignalKeys[key] = chosen ?? "";   // 空文字は「追従しない」
            });

            Grid.SetColumn(combo, 2);
            row.Children.Add(combo);

            var mark = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 16 };
            SetMark(mark, _received.Contains(id));
            Grid.SetColumn(mark, 3);
            row.Children.Add(mark);

            _signalMarks[id] = mark;
            LayerTable.Children.Add(row);
        }
    }

    private void CommitLayerName(int id, string text)
    {
        var current = _host.Keymap.Layers.FirstOrDefault(l => l.Index == id)?.Name ?? "";
        if (text.Trim() == current) return;

        Apply(config =>
        {
            var key = id.ToString(CultureInfo.InvariantCulture);

            // 空にしたら差し替えをやめ、キーマップに書かれた名前に戻す。
            if (string.IsNullOrWhiteSpace(text)) config.Zmk.LayerNames.Remove(key);
            else config.Zmk.LayerNames[key] = text.Trim();
        });
    }

    private void OnSignalReceived(int layerId)
    {
        _received.Add(layerId);
        if (_signalMarks.TryGetValue(layerId, out var mark)) SetMark(mark, true);
    }

    private static void SetMark(TextBlock mark, bool received)
    {
        mark.Text = received ? "✓" : "—";
        mark.Opacity = received ? 1.0 : 0.4;

        if (received) mark.Foreground = Brushes.SeaGreen;
        else mark.ClearValue(TextBlock.ForegroundProperty);
    }

    /// <summary>
    /// レイヤーの行の 5 列目。手で出すショートカットの入力欄と、既定に戻すボタン。
    ///
    /// Delete（割り当てを外す）と「既定に戻す」（上書きを消して Ctrl+Alt+番号 に戻す）は別物なので、
    /// 両方残してある。ボタンは列の幅に収まらないので記号にし、名前はツールチップと支援技術に渡す。
    /// </summary>
    private void AddShortcutCell(Grid row, int id)
    {
        var config = _host.Config;
        var key = id.ToString(CultureInfo.InvariantCulture);

        var cell = new DockPanel { LastChildFill = false };

        var box = new TextBox
        {
            Width = 190,
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            IsEnabled = config.EnableManualLayerKeys,
        };

        AutomationProperties.SetName(box, UiText.LayerShortcutUse(id));
        _capture.Attach(box, id);

        var reset = new Button
        {
            // 「元に戻す」の記号。Segoe UI には無い字なので、Windows に必ず入っている記号フォントから取る。
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12 },
            Width = 32,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = UiText.ResetToDefault,
            IsEnabled = config.EnableManualLayerKeys && config.LayerHotkeys.ContainsKey(key),
        };

        AutomationProperties.SetName(reset, $"L{id} {UiText.ResetToDefault}");

        reset.Click += (_, _) => Apply(c => c.LayerHotkeys.Remove(key));

        cell.Children.Add(box);
        cell.Children.Add(reset);

        _layerShortcutBoxes.Add(box);

        Grid.SetColumn(cell, 4);
        row.Children.Add(cell);
    }

    // ---- 文言 ----

    private void ApplyTexts()
    {
        // 項目を作り直すと開いているドロップダウンが閉じるので、言語が変わったときだけ入れ直す。
        if (_textsLanguage == Strings.Language) return;
        _textsLanguage = Strings.Language;

        _loading = true;

        try
        {
            Title = $"ZMK Keymap Overlay — {UiText.SettingsTitle}";

            NavDisplay.Content = UiText.PageDisplay;
            NavLayers.Content = UiText.PageLayers;
            NavKeyboard.Content = UiText.PageKeyboard;
            NavGeneral.Content = UiText.PageGeneral;

            SampleNote.Text = UiText.SampleNote;
            GitHubSection.Text = UiText.SectionGitHub;
            GitHubNote.Text = UiText.GitHubNote;
            GitHubFetch.Content = UiText.GitHubFetch;
            GitHubKeymapLabel.Text = UiText.GitHubKeymapLabel;
            GitHubUse.Content = UiText.GitHubUse;
            GitHubRefresh.Content = UiText.GitHubRefresh;
            KeymapSection.Text = UiText.SectionKeymap;
            KeymapFileLabel.Text = UiText.KeymapFileLabel;
            KeymapBrowse.Content = UiText.Browse;
            PhysicalLayoutSection.Text = UiText.SectionPhysicalLayout;
            LayoutFileLabel.Text = UiText.LayoutFileLabel;
            LayoutBrowse.Content = UiText.Browse;
            LayoutAuto.Content = UiText.UseKeymapLayout;
            ReloadButton.Content = UiText.ReloadKeymap;
            ZmkLayoutSection.Text = UiText.SectionZmkLayout;
            ZmkLayoutNote.Text = UiText.ZmkLayoutNote;
            ZmkShieldLabel.Text = UiText.ZmkShieldLabel;
            ZmkFetch.Content = UiText.ZmkFetch;
            HostLayoutSection.Text = UiText.SectionHostLayout;
            HostJis.Content = UiText.HostJis;
            HostUs.Content = UiText.HostUs;
            HostLayoutNote.Text = UiText.HostLayoutNote;
            WarningsSection.Text = UiText.SectionWarnings;
            NoWarnings.Text = UiText.NoWarnings;

            ShowWhenSection.Text = UiText.SectionShowWhen;
            ModeAlwaysRadio.Content = UiText.ModeAlways;
            ModeAlwaysNote.Text = UiText.ModeAlwaysTip;
            ModeLayersOnlyRadio.Content = UiText.ModeLayersOnly;
            ModeLayersOnlyNote.Text = UiText.ModeLayersOnlyTip;
            ModeSelectedRadio.Content = UiText.ModeSelected;
            ModeSelectedNote.Text = UiText.ModeSelectedTip;
            StatusSection.Text = UiText.SectionStatus;
            ToggleHotkeyLabel.Text = UiText.ToggleHotkeyLabel;
            DisplayHotkeyHint.Text = UiText.HotkeyHint;
            MarginCenterNote.Text = UiText.MarginUnusedAtCenter;
            LookSection.Text = UiText.SectionLook;
            SizeLabel.Text = UiText.SizeLabel;
            OpacityLabel.Text = UiText.OpacityLabel;
            PositionLabel.Text = UiText.PositionLabel;
            MarginLabel.Text = UiText.MarginLabel;
            PreviewSection.Text = UiText.SectionPreview;
            ClickableSection.Text = UiText.SectionClickable;
            ClickableBox.Content = UiText.Clickable;
            ClickableNote.Text = UiText.ClickableNote;
            ResetOffsetNote.Text = UiText.ResetOffsetNote;
            ResetOffsetButton.Content = UiText.ResetOffset;
            ClickThroughHotkeyLabel.Text = UiText.ClickThroughHotkeyLabel;
            ClickThroughHotkeyNote.Text = UiText.ClickThroughHotkeyNote;

            var position = PositionBox.SelectedIndex;
            PositionBox.Items.Clear();
            foreach (var p in Positions) PositionBox.Items.Add(UiText.PositionName(p));
            PositionBox.SelectedIndex = position;

            // 項目名は Label ではなく TextBlock なので、入力欄と結び付かない。
            // 名前の無いスライダーや入力欄が並ぶことになるため、ここで入れる。
            AutomationProperties.SetName(SizeSlider, UiText.SizeLabel);
            AutomationProperties.SetName(OpacitySlider, UiText.OpacityLabel);
            AutomationProperties.SetName(MarginSlider, UiText.MarginLabel);
            AutomationProperties.SetName(PositionBox, UiText.PositionLabel);
            AutomationProperties.SetName(GitHubUrl, UiText.SectionGitHub);
            AutomationProperties.SetName(GitHubKeymapBox, UiText.GitHubKeymapLabel);
            AutomationProperties.SetName(ZmkShieldBox, UiText.ZmkShieldLabel);
            AutomationProperties.SetName(WarningsBox, UiText.SectionWarnings);
            AutomationProperties.SetName(HotkeyBox, UiText.ToggleHotkeyLabel);
            AutomationProperties.SetName(ClickThroughHotkeyBox, UiText.ClickThroughHotkeyLabel);
            AutomationProperties.SetName(LanguageBox, UiText.LanguageLabel);

            SyncExplain.Text = UiText.SyncExplain;
            OpenFirmwareSetup.Content = UiText.OpenFirmwareSetup;
            SyncEnabledBox.Content = UiText.SyncEnabled;
            SyncHoldRadio.Content = UiText.SyncHold;
            SyncToggleRadio.Content = UiText.SyncToggle;
            ManualKeysBox.Content = UiText.ManualKeys;
            ManualKeysNote.Text = UiText.ManualKeysNote;
            LayerTableSection.Text = UiText.SectionLayerTable;
            LayerHotkeyHint.Text = UiText.HotkeyHint;
            ColumnLayer.Text = UiText.ColumnLayer;
            ColumnName.Text = UiText.ColumnName;
            ColumnSignal.Text = UiText.ColumnSignal;
            ColumnTest.Text = UiText.ColumnTest;
            ColumnShortcut.Text = UiText.ColumnShortcut;
            SignalTestHint.Text = UiText.SignalTestHint;

            LanguageLabel.Text = UiText.LanguageLabel;
            var language = LanguageBox.SelectedIndex;
            LanguageBox.Items.Clear();
            LanguageBox.Items.Add(UiText.LanguageAuto);
            LanguageBox.Items.Add("日本語");
            LanguageBox.Items.Add("English");
            LanguageBox.SelectedIndex = language;

            RunAtLoginBox.Content = UiText.RunAtLogin;
            RunAtLoginNote.Text = UiText.RunAtLoginTip;
            ReadmeButton.Content = UiText.OpenReadme;
            ConfigDocButton.Content = UiText.OpenConfigDoc;
            AboutSection.Text = UiText.SectionAbout;
            ConfigFileLabel.Text = UiText.ConfigFileLabel;
            OpenFolderButton.Content = UiText.OpenFolder;
            VersionLabel.Text = UiText.VersionLabel;

            UpdatePageTitle();
        }
        finally
        {
            _loading = false;
        }
    }

    private void UpdatePageTitle()
    {
        if (Nav.SelectedItem is ListBoxItem item) PageTitle.Text = item.Content as string ?? "";
    }

    // ---- ページ ----

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;

        for (var i = 0; i < _pages.Length; i++)
            _pages[i].Visibility = i == Nav.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;

        UpdatePageTitle();
    }

    // ---- GitHub ----

    private async void OnGitHubFetch(object sender, RoutedEventArgs e)
    {
        if (!GitHubLocation.TryParse(GitHubUrl.Text, out var location))
        {
            ShowError(UiText.GitHubInvalidUrl);
            return;
        }

        await RunGitHubAsync(async () =>
        {
            var tree = await _gitHub.ListAsync(location);
            var candidates = tree.KeymapPaths;

            if (candidates.Count == 0)
            {
                ShowError(Strings.GitHubNoKeymap(location.FullName));
                return;
            }

            _gitHubLocation = location;
            _gitHubTree = tree;

            // URL がキーマップのファイルそのものを指していれば、それを使う。
            if (location.Path is { } path && candidates.Contains(path))
            {
                await UseGitHubKeymapAsync(path);
                return;
            }

            if (candidates.Count == 1)
            {
                await UseGitHubKeymapAsync(candidates[0]);
                return;
            }

            GitHubKeymapBox.ItemsSource = candidates;
            GitHubKeymapBox.SelectedIndex = 0;
            GitHubChoice.Visibility = Visibility.Visible;
            GitHubStatus.Text = UiText.GitHubChooseKeymap(candidates.Count);
        });
    }

    private async void OnGitHubUse(object sender, RoutedEventArgs e)
    {
        if (GitHubKeymapBox.SelectedItem is not string path) return;

        await RunGitHubAsync(() => UseGitHubKeymapAsync(path));
    }

    private async void OnGitHubRefresh(object sender, RoutedEventArgs e)
    {
        await RunGitHubAsync(async () =>
        {
            var next = _host.Config.Clone();
            var notice = await GitHubSync.RefreshAsync(next, _host.ConfigPath, _gitHub);
            ShowError(_host.TryApply(next, out var error, reloadKeymap: true) ? notice : error);
        });
    }

    private async Task UseGitHubKeymapAsync(string keymapPath)
    {
        if (_gitHubLocation is null || _gitHubTree is null) return;

        var next = _host.Config.Clone();
        var notice = await GitHubSync.UseAsync(next, _host.ConfigPath, _gitHub, _gitHubLocation, _gitHubTree, keymapPath);

        GitHubChoice.Visibility = Visibility.Collapsed;

        // キーマップは使えるが、キーの並びを ZMK 本体から取れなかったときは、その理由を残しておく。
        ShowError(_host.TryApply(next, out var error, reloadKeymap: true) ? notice : error);
    }

    /// <summary>通信中はボタンを押せなくし、失敗したら理由を出す。</summary>
    private async Task RunGitHubAsync(Func<Task> work)
    {
        GitHubFetch.IsEnabled = GitHubUse.IsEnabled = GitHubRefresh.IsEnabled = ZmkFetch.IsEnabled = false;
        GitHubStatus.Text = UiText.GitHubFetching;
        ShowError(null);

        try
        {
            await work();
        }
        catch (Exception ex) when (ex is GitHubSourceException or IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
        }
        finally
        {
            GitHubFetch.IsEnabled = GitHubUse.IsEnabled = GitHubRefresh.IsEnabled = ZmkFetch.IsEnabled = true;

            // 反映が済んでいればそちらが表示を入れ直す。済んでいなければ「取得しています」を消す。
            if (GitHubStatus.Text == UiText.GitHubFetching)
                GitHubStatus.Text = _host.Config.Zmk.IsGitHub
                    ? UiText.GitHubInUse(_host.Config.Zmk.GitHub.Repository, _host.Config.Zmk.GitHub.Branch,
                        _host.Config.Zmk.GitHub.KeymapPath ?? "")
                    : "";
        }
    }

    // ---- キーボード ----

    private async void OnBrowseKeymap(object sender, RoutedEventArgs e)
    {
        var (_, error) = await SourceActions.UseLocalKeymapAsync(this, _host);
        ShowError(error);
    }

    /// <summary>ZMK 本体からキーの並びを取る。通信するのはこのボタンを押したときだけ。</summary>
    private async void OnZmkFetch(object sender, RoutedEventArgs e)
    {
        var shield = ZmkShieldBox.Text;

        GitHubFetch.IsEnabled = GitHubUse.IsEnabled = GitHubRefresh.IsEnabled = ZmkFetch.IsEnabled = false;
        ZmkStatus.Text = UiText.ZmkFetching;
        ShowError(null);

        try
        {
            var problem = await SourceActions.FetchZmkLayoutAsync(_host, shield);

            ZmkStatus.Text = problem is null ? UiText.ZmkFetched(_host.Config.Zmk.Shield) : "";
            ShowError(problem);
        }
        catch (Exception ex) when (ex is GitHubSourceException or IOException or UnauthorizedAccessException)
        {
            ZmkStatus.Text = "";
            ShowError(ex.Message);
        }
        finally
        {
            GitHubFetch.IsEnabled = GitHubUse.IsEnabled = GitHubRefresh.IsEnabled = ZmkFetch.IsEnabled = true;
        }
    }

    private void OnBrowseLayout(object sender, RoutedEventArgs e)
    {
        var layout = PickFile(UiText.ChooseLayout, UiText.LayoutFilter, CurrentKeymapFolder());
        if (layout is null) return;

        Apply(config => config.Zmk.PhysicalLayoutFile = layout);
    }

    private void OnLayoutAuto(object sender, RoutedEventArgs e) =>
        Apply(config => config.Zmk.PhysicalLayoutFile = null);

    /// <summary>設定は変えずに、キーマップのファイルを読み直す。</summary>
    private void OnReload(object sender, RoutedEventArgs e) =>
        ShowError(_host.Reload(out var error) ? null : error);

    private void OnHostLayoutChecked(object sender, RoutedEventArgs e)
    {
        var us = HostUs.IsChecked == true;
        Apply(config => config.KeyboardLayout = us ? "us" : "jis");
    }

    private string? PickFile(string title, string filter, string? folder) =>
        SourceActions.PickFile(this, title, filter, folder);

    private string? CurrentKeymapFolder() => SourceActions.KeymapFolder(_host);

    private void OnOpenFirmwareSetup(object sender, RoutedEventArgs e) =>
        _host.OpenSetup(ZmkOverlay.App.Setup.SetupStep.Firmware);

    private string Resolve(string path) => ConfigPaths.Resolve(_host.ConfigPath, path);

    // ---- 表示 ----

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        var mode = sender == ModeAlwaysRadio ? DisplayModes.Always
            : sender == ModeSelectedRadio ? DisplayModes.SelectedLayers
            : DisplayModes.LayersOnly;

        Apply(config => config.DisplayMode = mode);
    }

    private void OnSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;

        var value = e.NewValue;
        SizeValue.Text = $"{value:0}px";
        ApplyLater(config => config.KeyUnitPx = value);
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;

        var value = Math.Round(e.NewValue);
        OpacityValue.Text = $"{value:0}%";
        ApplyLater(config => config.Opacity = value / 100.0);
    }

    private void OnMarginChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;

        var value = e.NewValue;
        MarginValue.Text = $"{value:0}px";
        ApplyLater(config => config.Margin = value);
    }

    private void OnPositionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = PositionBox.SelectedIndex;
        if (index < 0) return;

        // ドラッグで動かした分を残すと、選んだ場所に来ない。基準を選び直したら捨てる。
        Apply(config =>
        {
            config.Position = Positions[index];
            config.OffsetX = 0;
            config.OffsetY = 0;
        });
    }

    /// <summary>
    /// クリックとドラッグを受け取るかどうか。ほかの設定と違ってアプリ側に任せるのは、
    /// 切り替えたときにオーバーレイが消えてしまわないよう、いま出ているレイヤーを押さえる必要があるため。
    /// </summary>
    private void OnClickableClick(object sender, RoutedEventArgs e)
    {
        if (!_ready || _loading) return;

        _host.SetClickThrough(ClickableBox.IsChecked != true);
    }

    private void OnResetOffset(object sender, RoutedEventArgs e) =>
        Apply(config =>
        {
            config.OffsetX = 0;
            config.OffsetY = 0;
        });

    // ---- レイヤー ----

    private void OnSyncEnabledClick(object sender, RoutedEventArgs e)
    {
        var enabled = SyncEnabledBox.IsChecked == true;
        Apply(config => config.LayerSync.Enabled = enabled);
    }

    private void OnSyncModeChecked(object sender, RoutedEventArgs e)
    {
        var toggle = SyncToggleRadio.IsChecked == true;
        Apply(config => config.LayerSync.Mode = toggle ? "toggle" : "hold");
    }

    private void OnManualKeysClick(object sender, RoutedEventArgs e)
    {
        var enabled = ManualKeysBox.IsChecked == true;
        Apply(config => config.EnableManualLayerKeys = enabled);
    }

    // ---- 全般 ----

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = LanguageBox.SelectedIndex;
        if (index < 0) return;

        Apply(config => config.Language = Languages[index]);
    }

    private void OnRunAtLoginClick(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        var enable = RunAtLoginBox.IsChecked == true;
        ShowError(_host.TrySetRunAtLogin(enable, out var error) ? null : error);

        // 実体（スタートアップフォルダ）を見直して、チェックを実際の状態に合わせる。
        RunAtLoginBox.IsChecked = _host.RunAtLogin;
    }

    // 表示言語に合わせた版をブラウザで開く。画面に出していない設定（合図の間隔など）はそこで見てもらう。
    private void OnOpenReadme(object sender, RoutedEventArgs e) => SourceActions.OpenUrl(ProjectLinks.Readme);

    private void OnOpenConfigDoc(object sender, RoutedEventArgs e) =>
        SourceActions.OpenUrl(ProjectLinks.ConfigurationDoc);

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_host.ConfigPath}\"");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }
}
