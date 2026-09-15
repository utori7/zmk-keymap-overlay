using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ZmkOverlay.App.Render;
using ZmkOverlay.App.Text;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Text;

namespace ZmkOverlay.App.Settings;

public enum SettingsPage
{
    Keyboard,
    Display,
    Sync,
    Shortcuts,
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

    /// <summary>レイヤーごとのショートカット入力欄。Tag にレイヤー番号を持つ。</summary>
    private readonly List<TextBox> _layerShortcutBoxes = new();

    private IDisposable? _hotkeySuspension;
    private UiLanguage? _textsLanguage;

    /// <summary>InitializeComponent 中にも値変更イベントが飛ぶので、準備が終わるまで無視する。</summary>
    private bool _ready;

    /// <summary>画面に値を流し込んでいる最中。このあいだの変更イベントは利用者の操作ではない。</summary>
    private bool _loading;

    internal SettingsWindow(ISettingsHost host)
    {
        InitializeComponent();

        _host = host;
        _pages = new FrameworkElement[] { KeyboardPage, DisplayPage, SyncPage, ShortcutsPage, GeneralPage };

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
        Deactivated += (_, _) => EndHotkeyCapture();

        Closed += (_, _) =>
        {
            _host.Applied -= OnApplied;
            _host.SignalReceived -= OnSignalReceived;
            _debounce.Stop();
            EndHotkeyCapture();
        };

        VersionText.Text = Version();

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
            KeymapPath.Text = fromZmk ? Resolve(config.Zmk.KeymapFile!) : UiText.SampleKeymap;
            KeymapPath.ToolTip = KeymapPath.Text;

            // 指定が無いときは、どこで見つけたか（または推定したか）を見せる。
            // 推定だと分からないまま使うと、配置の違いに気づけない。
            var hasLayoutFile = !string.IsNullOrWhiteSpace(config.Zmk.PhysicalLayoutFile);
            LayoutPath.Text = !fromZmk ? UiText.SampleKeymap
                            : hasLayoutFile ? Resolve(config.Zmk.PhysicalLayoutFile!)
                            : _host.LayoutSource switch
                            {
                                ZmkOverlay.Core.Model.LayoutSource.Keymap => UiText.LayoutInKeymap,
                                ZmkOverlay.Core.Model.LayoutSource.FoundNearby => UiText.LayoutFoundNearby(_host.LayoutPath ?? ""),
                                ZmkOverlay.Core.Model.LayoutSource.Guessed => UiText.LayoutGuessedLabel,
                                _ => _host.LayoutPath ?? "",
                            };
            LayoutPath.ToolTip = LayoutPath.Text;
            LayoutBrowse.IsEnabled = fromZmk;
            LayoutAuto.IsEnabled = fromZmk && hasLayoutFile;

            var us = config.KeyboardLayout.Equals("us", StringComparison.OrdinalIgnoreCase);
            HostJis.IsChecked = !us;
            HostUs.IsChecked = us;

            LayerNamesNote.Text = fromZmk ? UiText.LayerNamesNote : UiText.SampleCannotChange;
            BuildLayerNames(fromZmk);

            var warnings = _host.Warnings;
            WarningsBox.Text = string.Join(Environment.NewLine, warnings);
            WarningsBox.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoWarnings.Visibility = warnings.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

            // 表示
            ModeLayersOnlyRadio.IsChecked = !config.IsAlwaysVisible;
            ModeAlwaysRadio.IsChecked = config.IsAlwaysVisible;

            SizeSlider.Value = config.KeyUnitPx;
            SizeValue.Text = $"{config.KeyUnitPx:0}px";
            OpacitySlider.Value = Math.Round(config.Opacity * 100);
            OpacityValue.Text = $"{config.Opacity * 100:0}%";
            MarginSlider.Value = config.Margin;
            MarginValue.Text = $"{config.Margin:0}px";

            var position = Array.FindIndex(Positions, p => p.Equals(config.Position, StringComparison.OrdinalIgnoreCase));
            PositionBox.SelectedIndex = Math.Max(0, position);

            RefreshPreview();

            // レイヤー追従
            SyncEnabledBox.IsChecked = config.LayerSync.Enabled;
            SyncHoldRadio.IsChecked = config.LayerSync.IsHoldMode;
            SyncToggleRadio.IsChecked = !config.LayerSync.IsHoldMode;
            SyncHoldRadio.IsEnabled = SyncToggleRadio.IsEnabled = config.LayerSync.Enabled;

            SignalNote.Text = fromZmk ? UiText.SignalAutoNote : UiText.SampleCannotChange;
            BuildSignalList(fromZmk && config.LayerSync.Enabled);

            // ショートカット。入力中は「押してください」を上書きしない。
            if (!HotkeyBox.IsKeyboardFocused) HotkeyBox.Text = ShortcutText(null);
            ManualKeysBox.IsChecked = config.EnableManualLayerKeys;
            BuildLayerShortcuts();

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

    private void BuildLayerNames(bool editable)
    {
        LayerNamesList.Children.Clear();

        foreach (var layer in _host.Keymap.Layers)
        {
            var id = layer.Index;

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

            row.Children.Add(new TextBlock
            {
                Text = $"L{id}",
                Width = 48,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var box = new TextBox
            {
                Text = layer.Name,
                Width = 260,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = editable,
            };

            box.LostFocus += (_, _) => CommitLayerName(id, box.Text);
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) CommitLayerName(id, box.Text);
            };

            row.Children.Add(box);
            LayerNamesList.Children.Add(row);
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

    private void BuildSignalList(bool editable)
    {
        SignalList.Children.Clear();
        _signalMarks.Clear();

        var layers = _host.Keymap.Layers;

        // ベースレイヤーは合図キーを持たない（何も押していない状態そのもの）。
        foreach (var layer in layers.Skip(1))
        {
            var id = layer.Index;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            row.Children.Add(new TextBlock
            {
                Text = $"L{id} {layer.Name}".TrimEnd(),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var combo = new ComboBox { Width = 140, HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = editable };
            combo.Items.Add(UiText.NoSignal);
            foreach (var key in SignalKeyChoices) combo.Items.Add(key);

            var selected = Array.FindIndex(SignalKeyChoices, k => k.Equals(layer.SignalKey, StringComparison.OrdinalIgnoreCase));
            combo.SelectedIndex = selected + 1;

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

            Grid.SetColumn(combo, 1);
            row.Children.Add(combo);

            var mark = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 16 };
            SetMark(mark, _received.Contains(id));
            Grid.SetColumn(mark, 2);
            row.Children.Add(mark);

            _signalMarks[id] = mark;
            SignalList.Children.Add(row);
        }
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

    private void BuildLayerShortcuts()
    {
        LayerShortcutList.Children.Clear();
        _layerShortcutBoxes.Clear();

        var config = _host.Config;

        foreach (var layer in _host.Keymap.Layers)
        {
            var id = layer.Index;
            var key = id.ToString(CultureInfo.InvariantCulture);

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };

            row.Children.Add(new TextBlock
            {
                Text = $"L{id} {layer.Name}".TrimEnd(),
                Width = 180,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var box = new TextBox
            {
                Tag = id,
                Width = 220,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                IsEnabled = config.EnableManualLayerKeys,
                Text = ShortcutText(id),
            };

            box.GotKeyboardFocus += OnHotkeyFocus;
            box.LostKeyboardFocus += OnHotkeyBlur;
            box.PreviewKeyDown += OnHotkeyKeyDown;

            var reset = new Button
            {
                Content = UiText.ResetToDefault,
                Margin = new Thickness(8, 0, 0, 0),
                IsEnabled = config.EnableManualLayerKeys && config.LayerHotkeys.ContainsKey(key),
            };

            reset.Click += (_, _) => Apply(c => c.LayerHotkeys.Remove(key));

            row.Children.Add(box);
            row.Children.Add(reset);

            _layerShortcutBoxes.Add(box);
            LayerShortcutList.Children.Add(row);
        }
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

            NavKeyboard.Content = UiText.PageKeyboard;
            NavDisplay.Content = UiText.PageDisplay;
            NavSync.Content = UiText.PageSync;
            NavShortcuts.Content = UiText.PageShortcuts;
            NavGeneral.Content = UiText.PageGeneral;

            SampleNote.Text = UiText.SampleNote;
            KeymapSection.Text = UiText.SectionKeymap;
            KeymapFileLabel.Text = UiText.KeymapFileLabel;
            KeymapBrowse.Content = UiText.Browse;
            LayoutFileLabel.Text = UiText.LayoutFileLabel;
            LayoutBrowse.Content = UiText.Browse;
            LayoutAuto.Content = UiText.UseKeymapLayout;
            ReloadButton.Content = UiText.ReloadKeymap;
            HostLayoutSection.Text = UiText.SectionHostLayout;
            HostJis.Content = UiText.HostJis;
            HostUs.Content = UiText.HostUs;
            HostLayoutNote.Text = UiText.HostLayoutNote;
            LayerNamesSection.Text = UiText.SectionLayerNames;
            WarningsSection.Text = UiText.SectionWarnings;
            NoWarnings.Text = UiText.NoWarnings;

            ShowWhenSection.Text = UiText.SectionShowWhen;
            ModeLayersOnlyRadio.Content = UiText.ModeLayersOnly;
            ModeLayersOnlyNote.Text = UiText.ModeLayersOnlyTip;
            ModeAlwaysRadio.Content = UiText.ModeAlways;
            ModeAlwaysNote.Text = UiText.ModeAlwaysTip;
            LookSection.Text = UiText.SectionLook;
            SizeLabel.Text = UiText.SizeLabel;
            OpacityLabel.Text = UiText.OpacityLabel;
            PositionLabel.Text = UiText.PositionLabel;
            MarginLabel.Text = UiText.MarginLabel;
            PreviewSection.Text = UiText.SectionPreview;

            var position = PositionBox.SelectedIndex;
            PositionBox.Items.Clear();
            foreach (var p in Positions) PositionBox.Items.Add(UiText.PositionName(p));
            PositionBox.SelectedIndex = position;

            SyncExplain.Text = UiText.SyncExplain;
            SyncEnabledBox.Content = UiText.SyncEnabled;
            SyncHoldRadio.Content = UiText.SyncHold;
            SyncToggleRadio.Content = UiText.SyncToggle;
            SignalSection.Text = UiText.SectionSignalKeys;
            ColumnLayer.Text = UiText.ColumnLayer;
            ColumnSignal.Text = UiText.ColumnSignal;
            ColumnTest.Text = UiText.ColumnTest;
            SignalTestHint.Text = UiText.SignalTestHint;

            HotkeyHint.Text = UiText.HotkeyHint;
            ToggleHotkeyLabel.Text = UiText.ToggleHotkeyLabel;
            LayerShortcutsSection.Text = UiText.SectionLayerShortcuts;
            ManualKeysBox.Content = UiText.ManualKeys;
            ManualKeysNote.Text = UiText.ManualKeysNote;

            LanguageLabel.Text = UiText.LanguageLabel;
            var language = LanguageBox.SelectedIndex;
            LanguageBox.Items.Clear();
            LanguageBox.Items.Add(UiText.LanguageAuto);
            LanguageBox.Items.Add("日本語");
            LanguageBox.Items.Add("English");
            LanguageBox.SelectedIndex = language;

            RunAtLoginBox.Content = UiText.RunAtLogin;
            RunAtLoginNote.Text = UiText.RunAtLoginTip;
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

    // ---- キーボード ----

    private void OnBrowseKeymap(object sender, RoutedEventArgs e)
    {
        var keymap = PickFile(UiText.ChooseKeymap, UiText.KeymapFilter, CurrentKeymapFolder());
        if (keymap is null) return;

        // 別のキーボードに切り替えることもあるので、前の物理レイアウトは引き継がない。
        var next = _host.Config.Clone();
        next.Zmk.KeymapFile = keymap;
        next.Zmk.PhysicalLayoutFile = null;

        if (_host.TryApply(next, out var error))
        {
            ShowError(null);
            return;
        }

        // 物理レイアウトがシールドの .dtsi に分かれている構成は多い。
        // 失敗の理由がそれなら、続けてそのファイルを選んでもらう。
        if (error != Strings.PhysicalLayoutMissing)
        {
            ShowError(error);
            return;
        }

        MessageBox.Show(this, UiText.LayoutNeeded, Title, MessageBoxButton.OK, MessageBoxImage.Information);

        var layout = PickFile(UiText.ChooseLayout, UiText.LayoutFilter, Path.GetDirectoryName(keymap));
        if (layout is null)
        {
            ShowError(error);
            return;
        }

        next.Zmk.PhysicalLayoutFile = layout;
        ShowError(_host.TryApply(next, out error) ? null : error);
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
    private void OnReload(object sender, RoutedEventArgs e) => Apply(_ => { });

    private void OnHostLayoutChecked(object sender, RoutedEventArgs e)
    {
        var us = HostUs.IsChecked == true;
        Apply(config => config.KeyboardLayout = us ? "us" : "jis");
    }

    private string? PickFile(string title, string filter, string? folder)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
        };

        if (folder is not null && Directory.Exists(folder)) dialog.InitialDirectory = folder;

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? CurrentKeymapFolder() =>
        _host.Config.Zmk.IsEnabled ? Path.GetDirectoryName(Resolve(_host.Config.Zmk.KeymapFile!)) : null;

    private string Resolve(string path) => ConfigPaths.Resolve(_host.ConfigPath, path);

    // ---- 表示 ----

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        var always = ModeAlwaysRadio.IsChecked == true;
        Apply(config => config.DisplayMode = always ? "always" : "layersOnly");
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

        Apply(config => config.Position = Positions[index]);
    }

    // ---- レイヤー追従 ----

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

    // ---- ショートカット ----
    //
    // 入力欄は「オーバーレイの有効 / 無効」の 1 つと、レイヤーごとの欄。
    // Tag が null なら前者、レイヤー番号なら後者。記録の仕方は共通。

    private string ShortcutText(object? target) =>
        target is int layerId
            ? _host.Config.ManualLayerHotkey(layerId)?.ToString() ?? UiText.NoShortcut
            : _host.Config.ToggleHotkey.ToString();

    private void OnHotkeyFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_ready || sender is not TextBox box) return;

        _hotkeySuspension ??= _host.SuspendHotkeys();
        box.Text = UiText.PressKeys;
    }

    private void OnHotkeyBlur(object sender, KeyboardFocusChangedEventArgs e) => EndHotkeyCapture();

    private void EndHotkeyCapture()
    {
        _hotkeySuspension?.Dispose();
        _hotkeySuspension = null;

        if (!_ready) return;

        // 入力をやめたら、「押してください」などの表示を実際の割り当てに戻す。
        HotkeyBox.Text = ShortcutText(null);
        foreach (var box in _layerShortcutBoxes) box.Text = ShortcutText(box.Tag);
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        var layerTarget = box.Tag as int?;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Tab は入力欄から抜ける手段として残す。
        if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;

        e.Handled = true;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
                or Key.ImeProcessed or Key.DeadCharProcessed)
            return;   // 修飾キーだけでは確定しない。本体のキーを待つ。

        if (key == Key.Escape)
        {
            Nav.Focus();
            return;
        }

        // レイヤーの欄は Delete / BackSpace で割り当てを外せる。
        // 有効 / 無効のキーは外させない。外すとアプリを呼び出す手段がトレイだけになる。
        if (layerTarget is { } clearedLayer
            && Keyboard.Modifiers == ModifierKeys.None
            && key is Key.Delete or Key.Back)
        {
            Nav.Focus();
            Apply(config => config.LayerHotkeys[clearedLayer.ToString(CultureInfo.InvariantCulture)] = new HotkeySpec());
            return;
        }

        var modifiers = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers.Add("Win");

        // 修飾なしの普通のキーを取ると、そのキーが一切打てなくなる。
        if (modifiers.Count == 0 && key is not (>= Key.F13 and <= Key.F24))
        {
            box.Text = UiText.NeedModifier;
            return;
        }

        var name = key is >= Key.D0 and <= Key.D9
            ? ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture)
            : key.ToString();

        var spec = new HotkeySpec { Modifiers = modifiers, Key = name };

        // 同じ組み合わせを 2 か所に登録すると、後から登録したほうが黙って効かなくなる。
        // 入力欄に留まったまま理由を見せ、別の組み合わせを押してもらう。
        if (FindConflict(spec, layerTarget) is { } conflict)
        {
            box.Text = conflict;
            return;
        }

        // 先に入力欄から抜けてホットキーの一時解除を終えてから反映する。
        Nav.Focus();

        if (layerTarget is { } layerId)
            Apply(config => config.LayerHotkeys[layerId.ToString(CultureInfo.InvariantCulture)] = spec);
        else
            Apply(config => config.ToggleHotkey = spec);
    }

    /// <summary>その組み合わせがすでに何に使われているか。空いていれば null。</summary>
    private string? FindConflict(HotkeySpec spec, int? layerTarget)
    {
        var config = _host.Config;

        if (layerTarget is not null && spec.SameAs(config.ToggleHotkey))
            return UiText.ShortcutConflict(spec.ToString(), UiText.ToggleHotkeyLabel);

        foreach (var layer in _host.Keymap.Layers)
        {
            if (layer.Index != layerTarget
                && config.ManualLayerHotkey(layer.Index) is { } other
                && spec.SameAs(other))
                return UiText.ShortcutConflict(spec.ToString(), UiText.LayerShortcutUse(layer.Index));

            if (config.LayerSync.Enabled
                && layer.SignalKey is { } signal
                && spec.SameAs(new HotkeySpec { Key = signal }))
                return UiText.ShortcutConflict(spec.ToString(), UiText.SignalKeyUse(layer.Index));
        }

        return null;
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

    private static string Version()
    {
        var version = typeof(SettingsWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

        // SDK はビルド時のコミットを "+abcdef" の形で後ろに付ける。画面にはいらない。
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
