using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ZmkOverlay.App.Render;
using ZmkOverlay.App.Settings;
using ZmkOverlay.App.Text;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Dts;
using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Text;
using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.App.Setup;

public enum SetupStep
{
    Welcome,
    Source,
    Shape,
    Firmware,
    Test,
    Done,
}

/// <summary>
/// 初めて使う人のための案内。設定ファイルが無い状態で起動したときに開き、
/// トレイの「初期設定をやり直す…」や設定画面からも開ける。
///
/// 設定画面と同じく、いまの設定の複製を書き換えて <see cref="ISettingsHost.TryApply"/> に渡すだけで、
/// 反映の手順は知らない。どのステップも、その場で反映されてオーバーレイに出る。
/// </summary>
public partial class SetupWindow : Window
{
    private static readonly string[] Languages = { "auto", "ja", "en" };

    private readonly ISettingsHost _host;
    private readonly FrameworkElement[] _pages;
    private GitHubSource _gitHub => _host.GitHub;

    private GitHubLocation? _gitHubLocation;
    private GitHubTree? _gitHubTree;

    /// <summary>用意した書き換えと、その元にしたキーマップの中身。</summary>
    private KeymapPatch? _patch;
    private string? _patchedFrom;

    private readonly HashSet<int> _received = new();
    private readonly Dictionary<int, TextBlock> _testMarks = new();

    private SetupStep _step;
    private UiLanguage? _textsLanguage;
    private bool _ready;
    private bool _loading;

    internal SetupWindow(ISettingsHost host)
    {
        InitializeComponent();

        _host = host;
        _pages = new FrameworkElement[] { WelcomePage, SourcePage, ShapePage, FirmwarePage, TestPage, DonePage };

        _host.Applied += OnApplied;
        _host.SignalReceived += OnSignalReceived;

        Closed += (_, _) =>
        {
            _host.Applied -= OnApplied;
            _host.SignalReceived -= OnSignalReceived;
        };

        _ready = true;

        ApplyTexts();
        ShowStep(SetupStep.Welcome);
    }

    public void ShowStep(SetupStep step)
    {
        _step = step;

        for (var i = 0; i < _pages.Length; i++)
            _pages[i].Visibility = i == (int)step ? Visibility.Visible : Visibility.Collapsed;

        StepText.Text = UiText.SetupStepOf((int)step + 1, _pages.Length);
        BackButton.Visibility = step == SetupStep.Welcome ? Visibility.Hidden : Visibility.Visible;
        NextButton.Content = step == SetupStep.Done ? UiText.SetupFinish : UiText.SetupNext;
        LaterButton.Visibility = step == SetupStep.Done ? Visibility.Hidden : Visibility.Visible;

        ShowError(null);
        RefreshStep();
    }

    /// <summary>
    /// いまのステップの下に知らせを出す（キーマップを読めずにサンプルで起動した、など）。
    /// ステップを移ると消える。
    /// </summary>
    public void ShowNotice(string message) => ShowError(message);

    // ---- 共通 ----

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_step == SetupStep.Done) Close();
        else ShowStep(_step + 1);
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_step > SetupStep.Welcome) ShowStep(_step - 1);
    }

    /// <summary>途中でやめても、そこまでの設定は反映・保存済み。続きはトレイからいつでも開ける。</summary>
    private void OnLater(object sender, RoutedEventArgs e) => Close();

    private bool Apply(Action<AppConfig> change)
    {
        if (!_ready || _loading) return false;

        var next = _host.Config.Clone();
        change(next);

        var applied = _host.TryApply(next, out var error);
        ShowError(error);
        return applied;
    }

    /// <summary>反映の完了は操作の途中で届くことがあるので、画面の作り直しは一段遅らせる。</summary>
    private void OnApplied() =>
        Dispatcher.BeginInvoke(() =>
        {
            ApplyTexts();
            RefreshStep();
        });

    private void ShowError(string? message)
    {
        ErrorText.Text = message ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshStep()
    {
        _loading = true;

        try
        {
            switch (_step)
            {
                case SetupStep.Welcome:
                    var language = Array.FindIndex(Languages,
                        l => l.Equals(_host.Config.Language, StringComparison.OrdinalIgnoreCase));
                    WelcomeLanguage.SelectedIndex = Math.Max(0, language);
                    break;

                case SetupStep.Source:
                    SourceStatus.Text = UiText.SourceCurrent(SourceActions.SourceDescription(_host));
                    if (_host.Config.Zmk.IsGitHub && string.IsNullOrWhiteSpace(GitHubUrl.Text))
                        GitHubUrl.Text = $"https://github.com/{_host.Config.Zmk.GitHub.Repository}";
                    break;

                case SetupStep.Shape:
                    RefreshShape();
                    break;

                case SetupStep.Firmware:
                    RefreshFirmware();
                    break;

                case SetupStep.Test:
                    BuildTestList();
                    break;

                case SetupStep.Done:
                    DoneLead.Text = UiText.DoneLead(_host.Config.ToggleHotkey.ToString());
                    DoneLayersOnly.IsChecked = !_host.Config.IsAlwaysVisible;
                    DoneAlways.IsChecked = _host.Config.IsAlwaysVisible;
                    DoneRunAtLogin.IsChecked = _host.RunAtLogin;
                    break;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    // ---- 1. ようこそ ----

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = WelcomeLanguage.SelectedIndex;
        if (index < 0) return;

        Apply(config => config.Language = Languages[index]);
    }

    // ---- 2. キーマップの場所 ----

    private async void OnGitHubFetch(object sender, RoutedEventArgs e)
    {
        if (!GitHubLocation.TryParse(GitHubUrl.Text, out var location))
        {
            ShowError(UiText.GitHubInvalidUrl);
            return;
        }

        await RunBusyAsync(SourceStatus, UiText.GitHubFetching, async () =>
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

            var chosen = location.Path is { } path && candidates.Contains(path) ? path
                       : candidates.Count == 1 ? candidates[0]
                       : null;

            if (chosen is not null)
            {
                await UseGitHubKeymapAsync(chosen);
                return;
            }

            GitHubKeymapBox.ItemsSource = candidates;
            GitHubKeymapBox.SelectedIndex = 0;
            GitHubChoice.Visibility = Visibility.Visible;
            SourceStatus.Text = UiText.GitHubChooseKeymap(candidates.Count);
        });
    }

    private async void OnGitHubUse(object sender, RoutedEventArgs e)
    {
        if (GitHubKeymapBox.SelectedItem is not string path) return;

        await RunBusyAsync(SourceStatus, UiText.GitHubFetching, () => UseGitHubKeymapAsync(path));
    }

    private async Task UseGitHubKeymapAsync(string keymapPath)
    {
        if (_gitHubLocation is null || _gitHubTree is null) return;

        var next = _host.Config.Clone();
        var notice = await GitHubSync.UseAsync(next, _host.ConfigPath, _gitHub, _gitHubLocation, _gitHubTree, keymapPath);

        GitHubChoice.Visibility = Visibility.Collapsed;

        if (!_host.TryApply(next, out var error, reloadKeymap: true))
        {
            ShowError(error);
            return;
        }

        ShowStep(SetupStep.Shape);

        // キーマップは使えるが、キーの並びを ZMK 本体から取れなかった。推定で表示していることを伝える。
        if (notice is not null) ShowNotice(notice);
    }

    private async void OnLocalBrowse(object sender, RoutedEventArgs e)
    {
        var (applied, error) = await SourceActions.UseLocalKeymapAsync(this, _host);

        if (applied) ShowStep(SetupStep.Shape);
        else ShowError(error);
    }

    private void OnSampleUse(object sender, RoutedEventArgs e)
    {
        if (SourceActions.UseSample(_host, out var error)) ShowStep(SetupStep.Shape);
        else ShowError(error);
    }

    // ---- 3. キーボードの形 ----

    private void RefreshShape()
    {
        var config = _host.Config;

        if (_host.Keymap.Layers.Count > 0)
        {
            var panel = KeymapRenderer.BuildPanels(config, _host.Layout, _host.Keymap)[0];
            panel.Opacity = config.Opacity;
            ShapePreview.Content = panel;
        }

        ShapeSource.Text = UiText.ShapeSourceLabel(SourceActions.LayoutDescription(_host));

        var fromZmk = config.Zmk.IsEnabled;
        ShapeBrowse.IsEnabled = fromZmk;
        ShapeAuto.IsEnabled = fromZmk && !string.IsNullOrWhiteSpace(config.Zmk.PhysicalLayoutFile);

        // 自分の zmk-config にキーの並びがあるなら、ZMK 本体から取る必要はない。
        ShapeZmkCard.Visibility = fromZmk && _host.LayoutSource is not (LayoutSource.FoundNearby or LayoutSource.Keymap)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(ShapeShield.Text))
            ShapeShield.Text = ZmkShieldSync.SuggestShield(config, _host.ConfigPath) ?? "";

        var us = config.KeyboardLayout.Equals("us", StringComparison.OrdinalIgnoreCase);
        ShapeJis.IsChecked = !us;
        ShapeUs.IsChecked = us;
    }

    private async void OnShapeZmkFetch(object sender, RoutedEventArgs e)
    {
        var shield = ShapeShield.Text;

        await RunBusyAsync(ShapeZmkStatus, UiText.ZmkFetching, async () =>
        {
            var problem = await SourceActions.FetchZmkLayoutAsync(_host, shield);

            if (problem is null) ShapeZmkStatus.Text = UiText.ZmkFetched(_host.Config.Zmk.Shield);
            else ShowError(problem);
        });
    }

    private void OnShapeBrowse(object sender, RoutedEventArgs e)
    {
        var layout = SourceActions.PickFile(this, UiText.ChooseLayout, UiText.LayoutFilter, SourceActions.KeymapFolder(_host));
        if (layout is not null) Apply(config => config.Zmk.PhysicalLayoutFile = layout);
    }

    private void OnShapeAuto(object sender, RoutedEventArgs e) =>
        Apply(config => config.Zmk.PhysicalLayoutFile = null);

    private void OnHostLayoutChecked(object sender, RoutedEventArgs e)
    {
        var us = ShapeUs.IsChecked == true;
        Apply(config => config.KeyboardLayout = us ? "us" : "jis");
    }

    // ---- 4. ファームの書き換え ----

    private void RefreshFirmware()
    {
        var config = _host.Config;

        FirmwareTable.Children.Clear();
        FirmwareGitHub.Visibility = Visibility.Collapsed;
        FirmwareLocal.Visibility = Visibility.Collapsed;
        FirmwareDetails.Visibility = Visibility.Collapsed;
        FirmwareExperimental.Visibility = Visibility.Collapsed;
        FirmwareUndo.Visibility = Visibility.Collapsed;
        _patch = null;

        if (!config.Zmk.IsEnabled)
        {
            FirmwareLead.Text = UiText.FirmwareLeadSample;
            return;
        }

        var keymapPath = ConfigPaths.Resolve(_host.ConfigPath, config.Zmk.KeymapFile!);

        try
        {
            _patchedFrom = File.ReadAllText(keymapPath);
            _patch = KeymapPatcher.Patch(_patchedFrom, keymapPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidDataException or DtsParseException)
        {
            ShowError(ex.Message);
            return;
        }

        // 書き換えが必要かは、書き換えた結果が今のファイルと違うかで決める。
        // 前にこのアプリで書き換えたキーマップは、もう合図キーを持っているので「準備できています」になる。
        var needed = _patch.Changed;

        FirmwareLead.Text = needed && _patch.Added.Count > 0 ? UiText.FirmwareLeadNeeded
                          : needed ? UiText.FirmwareLeadUpdate
                          : _patch.AlreadySignaled.Count > 0 ? UiText.FirmwareLeadReady
                          : UiText.FirmwareLeadNothing;

        foreach (var layer in _host.Keymap.Layers.Skip(1))
            FirmwareTable.Children.Add(Row($"L{layer.Index} {layer.Name}".TrimEnd(), LayerStatus(layer.Index)));

        FirmwareExperimental.Visibility = Visibility.Visible;
        FirmwareGitHub.Visibility = needed && config.Zmk.IsGitHub ? Visibility.Visible : Visibility.Collapsed;
        FirmwareLocal.Visibility = needed && !config.Zmk.IsGitHub ? Visibility.Visible : Visibility.Collapsed;
        FirmwareDetails.Visibility = needed ? Visibility.Visible : Visibility.Collapsed;
        ChangesBox.Visibility = Visibility.Collapsed;

        // このアプリの書き換えが入っていれば、取り除けるようにする。ビルドが失敗したときや、
        // 書き込んだあとで具合が悪いときに、GitHub のキーマップまで元に戻す手段が要る。
        var github = config.Zmk.IsGitHub;
        FirmwareUndo.Visibility = WithoutGenerated() is not null ? Visibility.Visible : Visibility.Collapsed;
        FirmwareUndoNote.Text = github ? UiText.FirmwareUndoGitHub : UiText.FirmwareUndoLocal;
        UndoCopyAndOpen.Visibility = github ? Visibility.Visible : Visibility.Collapsed;
        UndoLocal.Visibility = github ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>今のキーマップから、このアプリの書き換えを取り除いたもの。書き換えが入っていなければ null。</summary>
    private string? WithoutGenerated() =>
        _patch is not null && _patchedFrom is { } text && KeymapPatcher.GeneratedBlock(text) is not null
            ? KeymapPatcher.RemoveGenerated(text)
            : null;

    private string LayerStatus(int layer)
    {
        if (_patch is null) return "";

        if (_patch.AlreadySignaled.TryGetValue(layer, out var existing)) return UiText.FirmwareHasSignal(existing);
        if (_patch.Added.FirstOrDefault(a => a.Layer == layer) is { } added) return UiText.FirmwareWillAdd(added.SignalKey);
        if (_patch.Conditional.TryGetValue(layer, out var ifLayers)) return UiText.FirmwareConditional(ifLayers);
        if (_patch.Skipped.FirstOrDefault(s => s.Layer == layer) is { } skipped) return UiText.SkipReasonText(skipped);

        return "";
    }

    /// <summary>
    /// GitHub の編集画面に貼る内容をコピーして、その画面を開く。直前に取り直してから作るのは、利用者が GitHub 上で
    /// 先に何か直していた場合に、古い内容で上書きしてしまわないため。
    /// <paramref name="text"/> は取り直したあとに呼ぶ。null なら（取り直したら、もうその状態だった）何もしない。
    /// </summary>
    private async Task CopyAndOpenEditorAsync(Func<string?> text, string done)
    {
        await RunBusyAsync(FirmwareStatus, UiText.GitHubFetching, async () =>
        {
            var next = _host.Config.Clone();
            await GitHubSync.RefreshAsync(next, _host.ConfigPath, _gitHub);

            if (!_host.TryApply(next, out var error, reloadKeymap: true))
            {
                ShowError(error);
                return;
            }

            RefreshFirmware();
            if (text() is not { } content) return;

            try
            {
                Clipboard.SetText(content);
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                ShowError(ex.Message);
                return;
            }

            SourceActions.OpenUrl(GitHubSync.EditUrl(_host.Config.Zmk.GitHub));
            FirmwareStatus.Text = done;
        });
    }

    private async void OnCopyAndOpen(object sender, RoutedEventArgs e) =>
        await CopyAndOpenEditorAsync(() => _patch is { Changed: true } patch ? patch.Text : null, UiText.CopiedAndOpened);

    private async void OnUndoCopyAndOpen(object sender, RoutedEventArgs e) =>
        await CopyAndOpenEditorAsync(WithoutGenerated, UiText.UndoCopied);

    private void OnOpenActions(object sender, RoutedEventArgs e) =>
        SourceActions.OpenUrl(GitHubSync.ActionsUrl(_host.Config.Zmk.GitHub));

    private async void OnRecheckGitHub(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(FirmwareStatus, UiText.GitHubFetching, async () =>
        {
            var next = _host.Config.Clone();
            var notice = await GitHubSync.RefreshAsync(next, _host.ConfigPath, _gitHub);

            if (!_host.TryApply(next, out var error, reloadKeymap: true)) ShowError(error);
            else if (notice is not null) ShowNotice(notice);
        });
    }

    private void OnSaveOverwrite(object sender, RoutedEventArgs e)
    {
        if (_patch is not null) OverwriteKeymap(_patch.Text, UiText.ConfirmOverwrite, UiText.Saved);
    }

    private void OnUndoLocal(object sender, RoutedEventArgs e)
    {
        if (WithoutGenerated() is { } text) OverwriteKeymap(text, UiText.ConfirmUndo, UiText.Undone);
    }

    /// <summary>
    /// PC のキーマップを <paramref name="text"/> で置き換える。確かめてから、今のファイルを .bak に残して書く。
    /// <paramref name="confirm"/> と <paramref name="done"/> は、ファイルとバックアップの場所から文言を作る。
    /// </summary>
    private void OverwriteKeymap(string text, Func<string, string> confirm, Func<string, string> done)
    {
        var path = ConfigPaths.Resolve(_host.ConfigPath, _host.Config.Zmk.KeymapFile!);

        var answer = MessageBox.Show(this, confirm(path), Title, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        try
        {
            // 用意したあとでファイルが変わっていたら、古い内容から作った書き換えで上書きしない。
            if (File.ReadAllText(path) != _patchedFrom)
            {
                RefreshFirmware();
                ShowError(UiText.KeymapChangedMeanwhile);
                return;
            }

            var backup = path + ".bak";
            if (File.Exists(backup)) backup = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";

            File.Copy(path, backup);
            WriteLikeOriginal(path, text);

            FirmwareStatus.Text = done(backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
            return;
        }

        // 書き換えたキーマップを読み直す。書き換えたなら合図キーが見つかって「準備できています」に、
        // 取り除いたなら「書き換えが必要」に、表示が変わる。
        if (!_host.Reload(out var reloadError)) ShowError(reloadError);
    }

    private void OnSaveAs(object sender, RoutedEventArgs e)
    {
        if (_patch is null) return;

        var original = ConfigPaths.Resolve(_host.ConfigPath, _host.Config.Zmk.KeymapFile!);

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = UiText.SaveAs,
            Filter = UiText.KeymapFilter,
            FileName = Path.GetFileName(original),
            InitialDirectory = Path.GetDirectoryName(original),
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            if (string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(original), StringComparison.OrdinalIgnoreCase))
            {
                // 元のファイルを選んだなら、バックアップを取る上書きと同じ扱いにする。
                OnSaveOverwrite(sender, e);
                return;
            }

            File.WriteAllText(dialog.FileName, _patch.Text, new UTF8Encoding(false));
            FirmwareStatus.Text = UiText.SavedAs(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
        }
    }

    /// <summary>元のファイルに BOM が付いていたら付ける。ビルド環境によっては BOM の有無で差が出るため揃える。</summary>
    private static void WriteLikeOriginal(string path, string text)
    {
        var head = new byte[3];
        int read;
        using (var stream = File.OpenRead(path)) read = stream.Read(head, 0, 3);

        var hasBom = read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
        File.WriteAllText(path, text, new UTF8Encoding(hasBom));
    }

    private void OnShowChanges(object sender, RoutedEventArgs e)
    {
        if (_patch is null || _patchedFrom is null) return;

        if (ChangesBox.Visibility == Visibility.Visible)
        {
            ChangesBox.Visibility = Visibility.Collapsed;
            return;
        }

        var text = new StringBuilder();
        text.AppendLine(UiText.ChangedLinesHeader);

        foreach (var line in KeymapPatcher.ChangedLines(_patchedFrom, _patch.Text))
        {
            text.AppendLine($"  {line.Line,5}  - {line.Before.Trim()}");
            text.AppendLine($"  {"",5}  + {line.After.Trim()}");
        }

        text.AppendLine();
        text.AppendLine(UiText.AddedBlockHeader);
        text.AppendLine(KeymapPatcher.GeneratedBlock(_patch.Text));

        ChangesBox.Text = text.ToString();
        ChangesBox.Visibility = Visibility.Visible;
    }

    // ---- 5. 動作確認 ----

    private void BuildTestList()
    {
        TestList.Children.Clear();
        _testMarks.Clear();

        TestReport.Visibility = KeymapHasGeneratedBlock() ? Visibility.Visible : Visibility.Collapsed;

        var signaled = _host.Keymap.Layers.Skip(1).Where(l => l.SignalKey is not null).ToList();

        if (signaled.Count == 0 || !_host.Config.LayerSync.Enabled)
        {
            TestList.Children.Add(new TextBlock { Text = UiText.TestNothing, TextWrapping = TextWrapping.Wrap });
            return;
        }

        foreach (var layer in signaled)
        {
            var mark = new TextBlock { FontSize = 16 };
            SetMark(mark, _received.Contains(layer.Index));
            _testMarks[layer.Index] = mark;

            TestList.Children.Add(Row($"L{layer.Index} {layer.Name}".TrimEnd(), layer.SignalKey!, mark));
        }
    }

    /// <summary>今のキーマップに、このアプリの書き換えが入っているか。読めなければ false。</summary>
    private bool KeymapHasGeneratedBlock()
    {
        var zmk = _host.Config.Zmk;
        if (!zmk.IsEnabled) return false;

        try
        {
            var text = File.ReadAllText(ConfigPaths.Resolve(_host.ConfigPath, zmk.KeymapFile!));
            return KeymapPatcher.GeneratedBlock(text) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 書き換えの結果を報告するページを開く。キーボード名と ZMK の版は、分かれば入力欄に入れておく。
    /// 開くのはブラウザで、送るかどうかは開いたページで本人が決める。
    /// </summary>
    private void OnReport(object sender, RoutedEventArgs e)
    {
        var config = _host.Config;
        string? keyboard = null;
        string? zmkVersion = null;

        if (config.Zmk.IsEnabled)
        {
            keyboard = ZmkShieldSync.SuggestShield(config, _host.ConfigPath);

            // build.yaml が見つからないときの Revision は仮の main なので、入れない。
            var info = ZmkShieldSource.ReadBuildInfo(ConfigPaths.Resolve(_host.ConfigPath, config.Zmk.KeymapFile!));
            if (info.Shields.Count > 0) zmkVersion = info.Revision;
        }

        SourceActions.OpenUrl(ProjectLinks.KeyboardUpdateReport(keyboard, zmkVersion));
    }

    private void OnSignalReceived(int layerId)
    {
        _received.Add(layerId);
        if (_testMarks.TryGetValue(layerId, out var mark)) SetMark(mark, true);
    }

    private static void SetMark(TextBlock mark, bool received)
    {
        mark.Text = received ? "✓" : "—";
        mark.Opacity = received ? 1.0 : 0.4;

        if (received) mark.Foreground = Brushes.SeaGreen;
        else mark.ClearValue(TextBlock.ForegroundProperty);
    }

    // ---- 6. 完了 ----

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        var always = DoneAlways.IsChecked == true;
        Apply(config => config.DisplayMode = always ? "always" : "layersOnly");
    }

    private void OnRunAtLoginClick(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        ShowError(_host.TrySetRunAtLogin(DoneRunAtLogin.IsChecked == true, out var error) ? null : error);
        DoneRunAtLogin.IsChecked = _host.RunAtLogin;
    }

    // ---- 部品 ----

    /// <summary>「名前 | 状態 | 印」の 1 行。</summary>
    private static UIElement Row(string name, string status, UIElement? mark = null)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });

        var statusText = new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
        Grid.SetColumn(statusText, 1);
        row.Children.Add(statusText);

        if (mark is not null)
        {
            Grid.SetColumn(mark, 2);
            row.Children.Add(mark);
        }

        return row;
    }

    /// <summary>通信中はボタンを押せなくし、失敗したら理由を出す。</summary>
    private async Task RunBusyAsync(TextBlock status, string busyText, Func<Task> work)
    {
        var buttons = new[]
        {
            GitHubFetch, GitHubUse, CopyAndOpen, RecheckGitHub, UndoCopyAndOpen, ShapeZmkFetch, NextButton, BackButton,
        };
        foreach (var button in buttons) button.IsEnabled = false;

        status.Text = busyText;
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
            foreach (var button in buttons) button.IsEnabled = true;
            if (status.Text == busyText) status.Text = "";
        }
    }

    // ---- 文言 ----

    private void ApplyTexts()
    {
        if (_textsLanguage == Strings.Language) return;
        _textsLanguage = Strings.Language;

        _loading = true;

        try
        {
            Title = $"ZMK Keymap Overlay — {UiText.SetupTitle}";
            BackButton.Content = UiText.SetupBack;
            LaterButton.Content = UiText.SetupLater;
            NextButton.Content = _step == SetupStep.Done ? UiText.SetupFinish : UiText.SetupNext;
            StepText.Text = UiText.SetupStepOf((int)_step + 1, _pages.Length);

            WelcomeTitle.Text = UiText.WelcomeTitle;
            WelcomeLead.Text = UiText.WelcomeLead;
            WelcomeSteps.Text = UiText.WelcomeSteps;
            WelcomeLanguageLabel.Text = UiText.LanguageLabel;

            var language = WelcomeLanguage.SelectedIndex;
            WelcomeLanguage.Items.Clear();
            WelcomeLanguage.Items.Add(UiText.LanguageAuto);
            WelcomeLanguage.Items.Add("日本語");
            WelcomeLanguage.Items.Add("English");
            WelcomeLanguage.SelectedIndex = language;

            SourceTitle.Text = UiText.SourceTitle;
            SourceLead.Text = UiText.SourceLead;
            GitHubChoiceTitle.Text = UiText.ChoiceGitHubTitle;
            GitHubChoiceNote.Text = UiText.ChoiceGitHubNote;
            GitHubFetch.Content = UiText.GitHubFetch;
            GitHubUse.Content = UiText.GitHubUse;
            LocalChoiceTitle.Text = UiText.ChoiceLocalTitle;
            LocalChoiceNote.Text = UiText.ChoiceLocalNote;
            LocalBrowse.Content = UiText.Browse;
            SampleChoiceTitle.Text = UiText.ChoiceSampleTitle;
            SampleChoiceNote.Text = UiText.ChoiceSampleNote;
            SampleUse.Content = UiText.UseSample;

            ShapeTitle.Text = UiText.ShapeTitle;
            ShapeLead.Text = UiText.ShapeLead;
            ShapeBrowse.Content = UiText.ShapeBrowse;
            ShapeAuto.Content = UiText.UseKeymapLayout;
            ShapeHostTitle.Text = UiText.SectionHostLayout;
            ShapeJis.Content = UiText.HostJis;
            ShapeUs.Content = UiText.HostUs;
            ShapeHostNote.Text = UiText.HostLayoutNote;
            ShapeZmkTitle.Text = UiText.SectionZmkLayout;
            ShapeZmkNote.Text = UiText.ZmkLayoutNote;
            ShapeShieldLabel.Text = UiText.ZmkShieldLabel;
            ShapeZmkFetch.Content = UiText.ZmkFetch;

            FirmwareTitle.Text = UiText.FirmwareTitle;
            FirmwareBadge.Text = UiText.ExperimentalBadge;
            FirmwareExperimental.Text = UiText.FirmwareExperimental;
            FirmwareBackup.Text = UiText.FirmwareBackup;
            FirmwareGitHubSteps.Text = UiText.FirmwareGitHubSteps;
            CopyAndOpen.Content = UiText.CopyAndOpenEditor;
            OpenActions.Content = UiText.OpenActions;
            RecheckGitHub.Content = UiText.RecheckGitHub;
            FirmwareLocalSteps.Text = UiText.FirmwareLocalSteps;
            SaveOverwrite.Content = UiText.SaveOverwrite;
            SaveAs.Content = UiText.SaveAs;
            ShowChanges.Content = UiText.ShowChanges;
            FirmwareFlashNote.Text = UiText.FirmwareFlashNote;
            FirmwareUndoTitle.Text = UiText.FirmwareUndoTitle;
            UndoCopyAndOpen.Content = UiText.UndoCopyAndOpen;
            UndoLocal.Content = UiText.UndoLocal;
            ReportProblem.Content = UiText.ReportProblem;

            TestTitle.Text = UiText.TestTitle;
            TestLead.Text = UiText.TestLead;
            TestReportNote.Text = UiText.TestReportNote;
            ReportResult.Content = UiText.ReportResult;

            DoneTitle.Text = UiText.DoneTitle;
            DoneModeTitle.Text = UiText.SectionShowWhen;
            DoneLayersOnly.Content = UiText.ModeLayersOnly;
            DoneLayersOnlyNote.Text = UiText.ModeLayersOnlyTip;
            DoneAlways.Content = UiText.ModeAlways;
            DoneAlwaysNote.Text = UiText.ModeAlwaysTip;
            DoneRunAtLogin.Content = UiText.RunAtLogin;
            DoneRunAtLoginNote.Text = UiText.RunAtLoginTip;
        }
        finally
        {
            _loading = false;
        }
    }
}
