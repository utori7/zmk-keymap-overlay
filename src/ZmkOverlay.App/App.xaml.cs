using System;
using System.Collections.Generic;
using System.Windows;
using ZmkOverlay.App.Interop;
using ZmkOverlay.App.Overlay;
using ZmkOverlay.App.Settings;
using ZmkOverlay.App.Setup;
using ZmkOverlay.App.Text;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Text;
using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.App;

public partial class App : Application, ISettingsHost
{
    private HotKeyService? _hotKeys;
    private OverlayController? _overlay;
    private LayerSyncService? _layerSync;
    private TrayIcon? _tray;
    private SettingsWindow? _settings;
    private SetupWindow? _setup;

    private IDisposable? _toggleRegistration;
    private readonly List<IDisposable> _layerRegistrations = new();

    /// <summary>ショートカットの入力中などで、ホットキーを外している数。0 のときだけ登録する。</summary>
    private int _hotkeySuspensions;

    /// <summary>いま効いている設定と、それで読んだキーマップ。</summary>
    private AppConfig _config = new();
    private PhysicalLayout _layout = new();
    private Keymap _keymap = new();
    private IReadOnlyList<string> _warnings = Array.Empty<string>();
    private LoadedKeymap _loaded = new();

    private string _configPath = "";
    private string? _configOverride;

    private readonly GitHubSource _gitHub = new();

    // 設定画面で値を動かすたびに作り直すので、同じ失敗は繰り返し知らせない。
    private string? _toggleFailureNotified;
    private string _syncFailuresNotified = "";
    private string _layerFailuresNotified = "";

    public event Action? Applied;

    public event Action<int>? SignalReceived;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            ShowError(UiText.UnexpectedError, args.Exception);
            args.Handled = true;
        };

        try
        {
            // --config で設定ファイルを明示できる。既定の探索場所を汚さずに
            // 別のキーマップを試せるようにするため。
            var configIndex = Array.FindIndex(e.Args, a => a is "--config");
            if (configIndex >= 0 && configIndex + 1 < e.Args.Length)
                _configOverride = System.IO.Path.GetFullPath(e.Args[configIndex + 1]);

            if (TryRunRenderMode(e.Args)) return;
            if (TryRunRenderWindowMode(e.Args)) return;
            if (TryRunStartupMode(e.Args)) return;

            Initialize();
        }
        catch (Exception ex)
        {
            ShowError(UiText.StartupFailed, ex);
            Shutdown(1);
        }
    }

    /// <summary>
    /// 開発用: --render &lt;出力ディレクトリ&gt; で全レイヤーを PNG に書き出して終了する。
    /// 常駐 UI を目視するにはホットキーを押すしかないので、確認手段として用意している。
    /// </summary>
    private bool TryRunRenderMode(string[] args)
    {
        var index = Array.FindIndex(args, a => a is "--render");
        if (index < 0) return false;

        var outputDir = index + 1 < args.Length ? args[index + 1] : "render-out";
        var (config, path, _) = LoadConfig();
        var loaded = LoadKeymap(config, path);
        var panels = Render.KeymapRenderer.BuildPanels(config, loaded.Layout, loaded.Keymap);

        for (var slot = 0; slot < loaded.Keymap.Layers.Count; slot++)
        {
            var layer = loaded.Keymap.Layers[slot];
            var file = System.IO.Path.Combine(outputDir, $"layer{layer.Index}.png");

            Render.OffscreenRenderer.RenderToPng(panels[slot], file);
            Console.WriteLine(file);
        }

        Shutdown();
        return true;
    }

    /// <summary>
    /// 開発用: --render-settings / --render-setup &lt;出力ディレクトリ&gt; で、設定画面または初期設定の
    /// 全ページを PNG に書き出して終了する。どちらもトレイからしか開けず、確かめるには常駐させる必要があるため。
    /// 画面の外に置いて描くので、利用者の画面には何も出ない。ページ全体が写るよう縦に長くしてある。
    /// </summary>
    private bool TryRunRenderWindowMode(string[] args)
    {
        var settingsIndex = Array.FindIndex(args, a => a is "--render-settings");
        var setupIndex = Array.FindIndex(args, a => a is "--render-setup");
        if (settingsIndex < 0 && setupIndex < 0) return false;

        var index = Math.Max(settingsIndex, setupIndex);
        var outputDir = index + 1 < args.Length ? args[index + 1] : "render-out";

        var (config, path, _) = LoadConfig();
        var loaded = LoadKeymap(config, path);

        _configPath = path;
        _config = config;
        _layout = loaded.Layout;
        _keymap = loaded.Keymap;
        _warnings = loaded.Warnings;
        _loaded = loaded;

        if (settingsIndex >= 0)
        {
            var settings = new SettingsWindow(this);
            RenderPages<SettingsPage>(settings, settings.ShowPage, "settings", outputDir);
        }
        else
        {
            var setup = new SetupWindow(this);
            RenderPages<SetupStep>(setup, setup.ShowStep, "setup", outputDir);
        }

        Shutdown();
        return true;
    }

    private static void RenderPages<TPage>(Window window, Action<TPage> show, string prefix, string outputDir)
        where TPage : struct, Enum
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.Height = 1300;
        window.Show();

        foreach (var page in Enum.GetValues<TPage>())
        {
            show(page);

            // 表示の切り替えとテーマの適用を描画まで進めてから写す。
            window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            window.UpdateLayout();

            var file = System.IO.Path.Combine(outputDir, $"{prefix}-{page.ToString().ToLowerInvariant()}.png");
            Render.OffscreenRenderer.RenderAsShown(window, file);
            Console.WriteLine(file);
        }

        window.Close();
    }

    /// <summary>
    /// <c>--startup on|off|status</c> で自動起動を操作して終了する。
    /// 常駐 UI を出さずに確かめられるようにするため。
    /// </summary>
    private bool TryRunStartupMode(string[] args)
    {
        var index = Array.FindIndex(args, a => a is "--startup");
        if (index < 0) return false;

        var action = index + 1 < args.Length ? args[index + 1] : "status";

        switch (action)
        {
            case "on":
                StartupEntry.Enable(_configOverride);
                break;

            case "off":
                StartupEntry.Disable();
                break;
        }

        Console.WriteLine($"{(StartupEntry.IsEnabled ? "on" : "off")}  {StartupEntry.ShortcutPath}");

        Shutdown();
        return true;
    }

    private void Initialize()
    {
        var (config, path, firstRun) = LoadConfig();
        var loaded = LoadKeymap(config, path);

        _configPath = path;
        _config = config;
        _layout = loaded.Layout;
        _keymap = loaded.Keymap;
        _warnings = loaded.Warnings;
        _loaded = loaded;

        _hotKeys = new HotKeyService();
        _overlay = new OverlayController(config, _layout, _keymap);

        _tray = new TrayIcon(
            toggle: ToggleOverlay,
            showLayer: ShowLayerByHand,
            openSettings: () => OpenSettings(SettingsPage.Keyboard),
            openSetup: () => OpenSetup(SetupStep.Welcome),
            reload: Reload,
            exit: () => Shutdown(),
            alwaysVisible: config.IsAlwaysVisible,
            setAlwaysVisible: SetAlwaysVisible,
            showWarnings: () => OpenSettings(SettingsPage.Keyboard));

        _tray.Configure(config.ToggleHotkey.ToString(), _overlay.Layers, id => config.ManualLayerHotkey(id)?.ToString());

        StartLayerSync(config);
        RefreshHotkeys();

        _overlay.Start();
        _tray.ShowState(_overlay.IsEnabled, notify: false);
        _tray.ReportWarnings(_warnings);

        // 初めて起動した人は、何をすればよいか分からない。サンプルを出したまま案内を開く。
        if (firstRun) OpenSetup(SetupStep.Welcome);
    }

    // ---- 読み込みと反映 ----

    /// <summary>設定ファイルを読む。無ければ既定値で作る（<c>Created</c> が true）。</summary>
    private (AppConfig Config, string Path, bool Created) LoadConfig()
    {
        var path = _configOverride ?? ConfigPaths.FindConfig();

        if (path is not null) return (AppConfig.Load(path), path, false);

        // 初回起動。既定値を書き出して、以後は利用者が編集できるようにする。
        var created = new AppConfig();
        path = ConfigPaths.DefaultWriteTarget();

        // exe の隣に書けず %APPDATA% に置いたときは、同梱のサンプルを相対パスでは指せない。
        var configFolder = System.IO.Path.GetDirectoryName(path);
        if (!string.Equals(configFolder, ConfigPaths.ExeDirectory, StringComparison.OrdinalIgnoreCase))
        {
            created.LayoutFile = System.IO.Path.Combine(ConfigPaths.ExeDirectory, created.LayoutFile);
            created.KeymapFile = System.IO.Path.Combine(ConfigPaths.ExeDirectory, created.KeymapFile);
        }

        created.Save(path);

        return (created, path, true);
    }

    /// <summary>キーのラベルも表示言語で変わるので、キーマップを読むより先に言語を決める。</summary>
    private static LoadedKeymap LoadKeymap(AppConfig config, string configPath)
    {
        Strings.Language = Strings.FromSetting(config.Language);

        // ZMK のソースを読むと、解釈できなかった箇所が警告として返る。
        // 黙って落とすと「なぜこのキーだけ変なのか」が分からなくなるので、呼び出し側で出す。
        return KeymapLoader.Load(config, configPath);
    }

    /// <summary>
    /// 設定を反映する唯一の経路。トレイ・設定画面・再読み込みはすべてここを通る。
    ///
    /// 先にキーマップを読み、読めたときだけ反映して保存する。
    /// 読めない設定を保存すると、次に起動したときに立ち上がらなくなるため。
    /// </summary>
    private bool TryApply(AppConfig config, bool save, out string? error)
    {
        var previousLanguage = Strings.Language;
        LoadedKeymap loaded;

        try
        {
            loaded = LoadKeymap(config, _configPath);
        }
        catch (Exception ex)
        {
            // 読み込みの前に言語だけ切り替わっているので戻す。
            Strings.Language = previousLanguage;
            error = ex.Message;
            return false;
        }

        Commit(config, loaded);
        if (Strings.Language != previousLanguage) _tray?.ApplyLanguage();

        error = null;
        if (!save) return true;

        try
        {
            config.Save(_configPath);
            return true;
        }
        catch (Exception ex)
        {
            // 保存できなくても今の動作は変えたままにする。次回起動で戻るだけ。
            error = $"{UiText.CannotSaveSettings}: {ex.Message}";
            return false;
        }
    }

    /// <summary>読み込めた設定を、オーバーレイ・トレイ・ホットキー・レイヤー追従に行き渡らせる。</summary>
    private void Commit(AppConfig config, LoadedKeymap loaded)
    {
        _config = config;
        _layout = loaded.Layout;
        _keymap = loaded.Keymap;
        _warnings = loaded.Warnings;
        _loaded = loaded;

        _overlay!.Reload(config, _layout, _keymap);

        _tray?.SyncAlwaysVisible(config.IsAlwaysVisible);
        _tray?.Configure(config.ToggleHotkey.ToString(), _overlay.Layers, id => config.ManualLayerHotkey(id)?.ToString());

        _layerSync?.Dispose();
        StartLayerSync(config);
        RefreshHotkeys();

        _tray?.ShowState(_overlay.IsEnabled, notify: false);
        _tray?.ReportWarnings(_warnings);

        Applied?.Invoke();
    }

    /// <summary>
    /// 設定ファイルとキーマップを読み直す。GitHub から読んでいるときは、先に取り直す。
    /// 通信できなくても保存済みのファイルで続ける。起動や表示を通信に依存させないため。
    /// </summary>
    private async void Reload()
    {
        try
        {
            var (config, path, _) = LoadConfig();
            _configPath = path;

            var fetched = false;
            if (config.Zmk.IsGitHub)
            {
                try
                {
                    await GitHubSync.RefreshAsync(config, path, _gitHub);
                    fetched = true;
                }
                catch (Exception ex) when (ex is GitHubSourceException or System.IO.IOException or UnauthorizedAccessException)
                {
                    _tray?.Notify(UiText.GitHubRefreshFailed, ex.Message, System.Windows.Forms.ToolTipIcon.Warning);
                }
            }

            // 取り直すとキーマップの保存先が書き換わることがあるので、そのときは保存する。
            // 読んだだけのときは保存しない（手で書いた設定ファイルの体裁を崩さないため）。
            if (!TryApply(config, save: fetched, out var error))
                ShowError(UiText.ReloadFailed, new InvalidOperationException(error));
        }
        catch (Exception ex)
        {
            ShowError(UiText.ReloadFailed, ex);
        }
    }

    /// <summary>
    /// トレイから切り替えたら設定ファイルにも書き戻す。次回起動で戻ってしまうと
    /// 「設定したのに効いていない」と見えるため。
    /// </summary>
    private void SetAlwaysVisible(bool always)
    {
        var next = _config.Clone();
        next.DisplayMode = always ? "always" : "layersOnly";

        if (!TryApply(next, save: true, out var error))
            _tray?.Notify(UiText.CannotSaveSettings, error ?? UiText.UnknownCause,
                System.Windows.Forms.ToolTipIcon.Warning);
    }

    private void OpenSettings(SettingsPage page)
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow(this);
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }

        _settings.ShowPage(page);

        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    private void OpenSetup(SetupStep step)
    {
        if (_setup is null)
        {
            _setup = new SetupWindow(this);
            _setup.Closed += (_, _) => _setup = null;
            _setup.Show();
        }

        _setup.ShowStep(step);

        if (_setup.WindowState == WindowState.Minimized) _setup.WindowState = WindowState.Normal;
        _setup.Activate();
    }

    // ---- 有効・無効 ----

    /// <summary>
    /// 常時表示の設定では、切り替えた結果が画面にそのまま出るので通知は要らない。
    /// 知らせるのは、無効にしても見た目が変わらない「L1 以上のときだけ表示」のときだけ。
    /// </summary>
    private void ToggleOverlay()
    {
        var enabled = _overlay!.Toggle();

        RefreshLayerHotKeys();
        _tray?.ShowState(enabled, notify: !_config.IsAlwaysVisible);
    }

    /// <summary>トレイからレイヤーを選んだ。無効だったら有効になるので、それに付随するものも合わせる。</summary>
    private void ShowLayerByHand(int layerId)
    {
        _overlay!.ShowManualLayer(layerId);

        RefreshLayerHotKeys();
        _tray?.ShowState(_overlay.IsEnabled, notify: false);
    }

    // ---- ホットキー ----

    private void RefreshHotkeys()
    {
        _toggleRegistration?.Dispose();
        _toggleRegistration = null;

        if (_hotkeySuspensions == 0) RegisterToggleHotKey(_config);

        RefreshLayerHotKeys();
    }

    /// <summary>
    /// レイヤー直接指定のホットキーは、表示中ではなく「有効なあいだ」押さえる。
    /// 表示に連動させると、L1 以上のときだけ表示する設定では
    /// 素の状態で押さえておらず使えないうえ、レイヤーキーを叩くたびに
    /// 登録と解除を繰り返すことになる。
    /// </summary>
    private void RefreshLayerHotKeys()
    {
        ReleaseLayerHotKeys();

        if (_hotkeySuspensions > 0 || _overlay is not { IsEnabled: true }) return;

        RegisterLayerHotKeys(_config);
    }

    private void RegisterToggleHotKey(AppConfig config)
    {
        _toggleRegistration = _hotKeys!.TryRegister(
            config.ToggleHotkey, ToggleOverlay, out var error);

        if (_toggleRegistration is not null)
        {
            _toggleFailureNotified = null;
            return;
        }

        var spec = config.ToggleHotkey.ToString();
        if (_toggleFailureNotified == spec) return;
        _toggleFailureNotified = spec;

        // ここが取れないとアプリを呼び出す手段がトレイだけになるので、必ず知らせる。
        _tray?.Notify(UiText.CannotRegisterHotkey, error ?? UiText.UnknownCause,
            System.Windows.Forms.ToolTipIcon.Warning);
    }

    private void StartLayerSync(AppConfig config)
    {
        _layerSync = new LayerSyncService(_hotKeys!, _overlay!, config.LayerSync);
        _layerSync.SignalReceived += layerId => SignalReceived?.Invoke(layerId);

        var failures = string.Join("\n", _layerSync.Start());

        // 合図キーが取れないレイヤーは黙って追従しなくなるだけなので、
        // 気づけるように知らせる。他のレイヤーは動き続ける。
        if (failures.Length > 0 && failures != _syncFailuresNotified)
            _tray?.Notify(UiText.LayersNotFollowed, failures, System.Windows.Forms.ToolTipIcon.Warning);

        _syncFailuresNotified = failures;
    }

    private void RegisterLayerHotKeys(AppConfig config)
    {
        var failures = new List<string>();

        foreach (var layerId in _overlay!.LayerIds)
        {
            if (config.ManualLayerHotkey(layerId) is not { } spec) continue;

            var id = layerId;
            var registration = _hotKeys!.TryRegister(spec, () => _overlay!.ShowManualLayer(id), out var error);

            // 取れないものがあっても他は動かす。組み合わせは利用者が選んだものなので、失敗は知らせる。
            if (registration is not null) _layerRegistrations.Add(registration);
            else failures.Add($"L{id}: {error}");
        }

        var summary = string.Join("\n", failures);
        if (summary.Length > 0 && summary != _layerFailuresNotified)
            _tray?.Notify(UiText.CannotRegisterHotkey, summary, System.Windows.Forms.ToolTipIcon.Warning);

        _layerFailuresNotified = summary;
    }

    private void ReleaseLayerHotKeys()
    {
        foreach (var registration in _layerRegistrations) registration.Dispose();
        _layerRegistrations.Clear();
    }

    // ---- 設定画面から見たアプリ ----

    AppConfig ISettingsHost.Config => _config;

    string ISettingsHost.ConfigPath => _configPath;

    PhysicalLayout ISettingsHost.Layout => _layout;

    Keymap ISettingsHost.Keymap => _keymap;

    IReadOnlyList<string> ISettingsHost.Warnings => _warnings;

    LayoutSource ISettingsHost.LayoutSource => _loaded.LayoutSource;

    string? ISettingsHost.LayoutPath => _loaded.LayoutPath;

    bool ISettingsHost.TryApply(AppConfig config, out string? error) => TryApply(config, save: true, out error);

    bool ISettingsHost.RunAtLogin => StartupEntry.IsEnabled;

    /// <summary>
    /// 自動起動の登録・解除。設定ファイルではなくスタートアップフォルダの
    /// 実体が唯一の情報源なので、呼び出し側は結果を <see cref="StartupEntry.IsEnabled"/> で見直す。
    /// </summary>
    bool ISettingsHost.TrySetRunAtLogin(bool enable, out string? error)
    {
        try
        {
            // --config で起動していたときだけ、その指定を引き継ぐ。
            if (enable) StartupEntry.Enable(_configOverride);
            else StartupEntry.Disable();

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    void ISettingsHost.OpenSetup(SetupStep step) => OpenSetup(step);

    IDisposable ISettingsHost.SuspendHotkeys()
    {
        if (_hotkeySuspensions++ == 0) RefreshHotkeys();
        return new HotkeySuspension(this);
    }

    private sealed class HotkeySuspension : IDisposable
    {
        private readonly App _app;
        private bool _released;

        public HotkeySuspension(App app) => _app = app;

        public void Dispose()
        {
            if (_released) return;
            _released = true;

            if (--_app._hotkeySuspensions == 0) _app.RefreshHotkeys();
        }
    }

    /// <summary>
    /// 画面に出すだけでなくファイルにも残す。常駐アプリなので、
    /// 起動に失敗したときにダイアログを見逃すと何も手がかりが残らない。
    /// </summary>
    private static void ShowError(string title, Exception ex)
    {
        var logPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ZmkOverlay-error.log");

        try
        {
            System.IO.File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // ログを残せなくても、ダイアログは出す。
        }

        MessageBox.Show(
            $"{ex.Message}{Environment.NewLine}{Environment.NewLine}{UiText.ErrorDetails(logPath)}",
            $"ZMK Keymap Overlay — {title}",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _layerSync?.Dispose();
        ReleaseLayerHotKeys();
        _toggleRegistration?.Dispose();
        _hotKeys?.Dispose();
        _tray?.Dispose();

        base.OnExit(e);
    }
}
