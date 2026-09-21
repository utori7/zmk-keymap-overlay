using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
    /// <summary>
    /// 同じ利用者のセッションで 1 つだけ動かすための名前。2 つ目が動くと、ホットキーと合図キーを取れずに
    /// 「登録できません」が並び、トレイのアイコンも 2 つになる。
    /// </summary>
    private const string InstanceName = @"Local\ZmkOverlay.Instance";

    /// <summary>2 つ目に起動されたときに、1 つ目へ「前に出て」と伝える合図。</summary>
    private const string ActivateName = @"Local\ZmkOverlay.Activate";

    private Mutex? _instance;
    private bool _ownsInstance;
    private EventWaitHandle? _activate;
    private RegisteredWaitHandle? _activateWait;

    private HotKeyService? _hotKeys;
    private OverlayController? _overlay;
    private LayerSyncService? _layerSync;
    private TrayIcon? _tray;
    private SettingsWindow? _settings;
    private SetupWindow? _setup;

    private IDisposable? _toggleRegistration;
    private IDisposable? _clickThroughRegistration;
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
    private string? _clickThroughFailureNotified;
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

        // 文字の入力に使われる組み合わせをホットキーにしない。判定には Windows の配列情報が要る。
        HotkeyRules.TypesCharacter = KeyboardLayouts.TypesCharacter;

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

            if (!ClaimSingleInstance())
            {
                Shutdown();
                return;
            }

            Initialize();
        }
        catch (Exception ex)
        {
            ShowError(UiText.StartupFailed, ex);
            Shutdown(1);
        }
    }

    /// <summary>
    /// 1 つ目の起動なら true。すでに動いていれば、そちらに設定画面を開かせて false。
    /// exe をもう一度ダブルクリックした人には、設定画面が出てくるように見える。
    /// </summary>
    private bool ClaimSingleInstance()
    {
        _instance = new Mutex(initiallyOwned: true, InstanceName, out var created);
        _ownsInstance = created;

        if (!created)
        {
            try
            {
                _ownsInstance = _instance.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // 前のアプリが終了処理を経ずに落ちた。持ち主はいないので、こちらが引き継ぐ。
                _ownsInstance = true;
            }
        }

        if (!_ownsInstance)
        {
            // 1 つ目が前に出られるように、前面に出る権利を渡してから知らせる。
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            if (EventWaitHandle.TryOpenExisting(ActivateName, out var existing))
            {
                using (existing) existing.Set();
            }

            _instance.Dispose();
            _instance = null;
            return false;
        }

        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(
            _activate,
            (_, _) => Dispatcher.BeginInvoke(BringToFront),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        return true;
    }

    /// <summary>もう一度起動された。初期設定の途中ならそれを、そうでなければ設定画面を前に出す。</summary>
    private void BringToFront()
    {
        if (_overlay is null) return;

        if (_setup is not null) OpenSetup(null);
        else OpenSettings(null);
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
        var (config, path, _, _) = LoadConfig(recover: false);
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

        var (config, path, _, _) = LoadConfig(recover: false);
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
            // 全ステップを順に出すだけなので、再開位置は覚えない（利用者の設定ファイルを書き換えないため）。
            var setup = new SetupWindow(this) { RenderOnly = true };
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

    /// <summary>
    /// 常駐を始める。
    ///
    /// キーマップが読めなくても起動をやめない。やめると設定画面も初期設定も開けず、利用者に残る手段が
    /// 「config.json を探して消す」だけになる。サンプルを表示して起動し、キーマップを選び直してもらう。
    /// 選び直すまで設定ファイルは書き換えないので、ファイルが戻ってくれば（USB メモリなど）次回はそのまま読める。
    /// </summary>
    private void Initialize()
    {
        var (config, path, firstRun, notice) = LoadConfig(recover: true);

        string? fellBack = null;
        LoadedKeymap loaded;

        try
        {
            loaded = LoadKeymap(config, path);
        }
        catch (Exception ex)
        {
            fellBack = UiText.StartupFellBack(ex.Message);
            config = WithSample(config, path);

            // サンプルまで読めない（data フォルダが無い）ときだけ、起動をやめる。
            var sample = LoadKeymap(config, path);
            loaded = WithFirstWarning(sample, fellBack);
        }

        _configPath = path;
        _config = config;
        _layout = loaded.Layout;
        _keymap = loaded.Keymap;
        _warnings = loaded.Warnings;
        _loaded = loaded;

        _hotKeys = new HotKeyService();
        _overlay = new OverlayController(config, _layout, _keymap);
        _overlay.LayerTabClicked += OnLayerTabClicked;
        _overlay.Dragged += OnOverlayDragged;

        _tray = new TrayIcon(
            toggle: ToggleOverlay,
            showLayer: ShowLayerByHand,
            openSettings: () => OpenSettings(null),
            openSetup: () => OpenSetup(ResumeSetupStep()),
            reload: Reload,
            exit: () => Shutdown(),
            displayMode: config.EffectiveDisplayMode,
            setDisplayMode: SetDisplayMode,
            showWarnings: () => OpenSettings(SettingsPage.Keyboard),
            clickThrough: config.ClickThrough,
            setClickThrough: SetClickThrough);

        ConfigureTray(config);

        StartLayerSync(config);
        RefreshHotkeys();

        _overlay.Start();
        _tray.ShowState(_overlay.IsEnabled, notify: false);
        _tray.ReportWarnings(_warnings);

        // exe のフォルダを移したあとも、サインイン時に起動できるように。
        StartupEntry.RepairIfMoved();

        if (notice is not null)
            _tray.Notify(UiText.SettingsFileReset, notice, System.Windows.Forms.ToolTipIcon.Warning);

        if (fellBack is not null)
        {
            _tray.Notify(UiText.KeymapNotLoaded, fellBack, System.Windows.Forms.ToolTipIcon.Warning,
                onClick: () => OpenSetup(SetupStep.Source, fellBack));
            OpenSetup(SetupStep.Source, fellBack);
        }
        else if (firstRun)
        {
            // 初めて起動した人は、何をすればよいか分からない。サンプルを出したまま案内を開く。
            OpenSetup(SetupStep.Welcome, notice, guideOnClose: true);
        }
    }

    // ---- 読み込みと反映 ----

    /// <summary>
    /// 設定ファイルを読む。無ければ既定値で作る（<c>Created</c> が true）。
    ///
    /// <paramref name="recover"/> のときは、起動できなくなる状態をここで直す。
    /// 読めないファイルは退避して作り直し（<c>Notice</c> に理由）、古い場所を指すサンプルのパスは向け直す。
    /// --config で明示されたファイルは開発用なので、退避しない。
    /// </summary>
    private (AppConfig Config, string Path, bool Created, string? Notice) LoadConfig(bool recover)
    {
        var path = _configOverride ?? ConfigPaths.FindConfig();
        string? notice = null;

        if (path is not null)
        {
            try
            {
                var config = AppConfig.Load(path);

                if (recover && ConfigRecovery.RepairSamplePaths(config, path, ConfigPaths.ExeDirectory))
                    TrySave(config, path);

                return (config, path, false, null);
            }
            catch (JsonException ex) when (recover && _configOverride is null)
            {
                var moved = ConfigRecovery.Quarantine(path, DateTime.Now);
                notice = UiText.ConfigWasBroken(moved, ex.Message);
            }
        }

        // 初回起動（または作り直し）。既定値を書き出して、以後は利用者が編集できるようにする。
        path ??= ConfigPaths.DefaultWriteTarget();

        var (layout, keymap) = ConfigPaths.SampleFiles(path);
        var created = new AppConfig
        {
            LayoutFile = layout,
            KeymapFile = keymap,

            // PC のキーボードに合わせる。記号の表示が変わるので、英語配列の人に日本語配列の表示を出さない。
            KeyboardLayout = KeyboardLayouts.DetectHostLayout(),

            // AltGr で文字を打つ配列では、Ctrl+Alt+K を取ると文字が打てなくなる。
            ToggleHotkey = HotkeyRules.ChooseDefaultToggle(),
        };

        created.Save(path);

        return (created, path, true, notice);
    }

    private static void TrySave(AppConfig config, string path)
    {
        try
        {
            config.Save(path);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // 直した内容を保存できなくても、今回は直した値で動く。次回も同じように直す。
        }
    }

    /// <summary>設定の中のキーマップだけを同梱のサンプルに差し替えた複製。</summary>
    private static AppConfig WithSample(AppConfig config, string configPath)
    {
        var (layout, keymap) = ConfigPaths.SampleFiles(configPath);

        var sample = config.Clone();
        sample.Zmk.KeymapFile = null;
        sample.Zmk.PhysicalLayoutFile = null;
        sample.Zmk.ShieldLayoutFolder = null;
        sample.Zmk.Source = "local";
        sample.LayoutFile = layout;
        sample.KeymapFile = keymap;
        return sample;
    }

    private static LoadedKeymap WithFirstWarning(LoadedKeymap loaded, string warning) =>
        new()
        {
            Layout = loaded.Layout,
            Keymap = loaded.Keymap,
            Warnings = new[] { warning }.Concat(loaded.Warnings).ToList(),
            FromZmkSource = loaded.FromZmkSource,
            LayoutSource = loaded.LayoutSource,
            LayoutPath = loaded.LayoutPath,
        };

    /// <summary>キーのラベルも表示言語で変わるので、キーマップを読むより先に言語を決める。</summary>
    private static LoadedKeymap LoadKeymap(AppConfig config, string configPath)
    {
        Strings.Language = Strings.FromSetting(config.Language);

        // ZMK のソースを読むと、解釈できなかった箇所が警告として返る。
        // 黙って落とすと「なぜこのキーだけ変なのか」が分からなくなるので、呼び出し側で出す。
        return KeymapLoader.Load(config, configPath);
    }

    /// <summary>キーマップの読み込み結果を左右する値。これが変わらなければ、ファイルを読み直す必要はない。</summary>
    private static string LoadInputs(AppConfig config) =>
        JsonSerializer.Serialize(
            new { config.LayoutFile, config.KeymapFile, config.Zmk, config.KeyboardLayout, config.Language },
            AppConfig.SerializerOptions);

    /// <summary>
    /// 設定を反映する唯一の経路。トレイ・設定画面・再読み込みはすべてここを通る。
    ///
    /// 先にキーマップを読み、読めたときだけ反映して保存する。
    /// 読めない設定を保存すると、次に起動したときに立ち上がらなくなるため。
    ///
    /// 大きさや位置のように読み込みに関係しない値だけが変わったときは、前回読んだものを使う。
    /// スライダーを動かすたびに、キーマップのフォルダを探し直さないように。
    /// </summary>
    private bool TryApply(AppConfig config, bool save, bool reload, out string? error)
    {
        var previousLanguage = Strings.Language;
        LoadedKeymap loaded;

        if (!reload && LoadInputs(config) == LoadInputs(_config))
        {
            loaded = _loaded;
        }
        else
        {
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
            // 戻ることを書かないと、直したつもりのまま次回に持ち越される。
            error = $"{UiText.CannotSaveSettings}: {ex.Message}" + Environment.NewLine + UiText.SaveFailedReverts;
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

        _tray?.SyncDisplayMode(config.EffectiveDisplayMode);
        _tray?.SyncClickThrough(config.ClickThrough);
        ConfigureTray(config);

        _layerSync?.Dispose();
        StartLayerSync(config);
        RefreshHotkeys();

        _tray?.ShowState(_overlay.IsEnabled, notify: false);
        _tray?.ReportWarnings(_warnings);

        Applied?.Invoke();
    }

    /// <summary>トレイのメニューには、実際に登録するショートカットだけを並べる。</summary>
    private void ConfigureTray(AppConfig config) =>
        _tray?.Configure(
            HotkeyRules.TypesCharacter(config.ToggleHotkey) ? "" : config.ToggleHotkey.ToString(),
            _overlay!.Layers,
            id => config.ManualLayerHotkey(id) is { } spec && !HotkeyRules.TypesCharacter(spec) ? spec.ToString() : null,
            setupInProgress: config.SetupStep is not null,
            clickThroughHotkey: ClickThroughHotkeyText(config));

    /// <summary>登録するときだけ、トレイにショートカットを出す。割り当てが無ければ null。</summary>
    private static string? ClickThroughHotkeyText(AppConfig config) =>
        config.EffectiveClickThroughHotkey is { } spec && !HotkeyRules.TypesCharacter(spec)
            ? spec.ToString()
            : null;

    /// <summary>
    /// トレイやバルーンから初期設定を開くとき、どのステップから始めるか。
    /// 途中で閉じていればその続きから（<see cref="AppConfig.SetupStep"/>）。
    /// </summary>
    private SetupStep ResumeSetupStep() =>
        _config.SetupStep is { } step && Enum.IsDefined(typeof(SetupStep), step)
            ? (SetupStep)step
            : SetupStep.Welcome;

    /// <summary>
    /// 設定ファイルとキーマップを読み直す。GitHub から読んでいるときは、先に取り直す。
    /// 通信できなくても保存済みのファイルで続ける。起動や表示を通信に依存させないため。
    /// </summary>
    private async void Reload()
    {
        try
        {
            var (config, path, _, _) = LoadConfig(recover: false);
            _configPath = path;

            var fetched = false;
            if (config.Zmk.IsGitHub)
            {
                try
                {
                    var notice = await GitHubSync.RefreshAsync(config, path, _gitHub);
                    fetched = true;

                    if (notice is not null)
                        _tray?.Notify(UiText.ZmkFetchProblem, notice, System.Windows.Forms.ToolTipIcon.Warning);
                }
                catch (Exception ex) when (ex is GitHubSourceException or System.IO.IOException or UnauthorizedAccessException)
                {
                    _tray?.Notify(UiText.GitHubRefreshFailed, ex.Message, System.Windows.Forms.ToolTipIcon.Warning);
                }
            }

            // 取り直すとキーマップの保存先が書き換わることがあるので、そのときは保存する。
            // 読んだだけのときは保存しない（手で書いた設定ファイルの体裁を崩さないため）。
            if (!TryApply(config, save: fetched, reload: true, out var error))
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
    private void SetDisplayMode(string mode)
    {
        var next = _config.Clone();
        next.DisplayMode = mode;

        if (!TryApply(next, save: true, reload: false, out var error))
            _tray?.Notify(UiText.CannotSaveSettings, error ?? UiText.UnknownCause,
                System.Windows.Forms.ToolTipIcon.Warning, onClick: () => OpenSettings(SettingsPage.Display));
    }

    /// <summary>
    /// オーバーレイがクリックとドラッグを受け取るかどうかを切り替える。
    /// トレイ・設定画面・ショートカットの 3 つの入口が、ここを通る。
    /// </summary>
    private void SetClickThrough(bool clickThrough)
    {
        // 触れるようにするための切り替えなのに、反映の途中で AtRest() が走って板が消えてしまう
        // （既定の「L1 以上のときだけ表示」では、レイヤーキーを押していない状態は非表示）。
        // 触る対象が無くならないよう、いま出ているレイヤーを手で選んだことにして押さえておく。
        if (!clickThrough) _overlay!.PinCurrentLayer();

        var next = _config.Clone();
        next.ClickThrough = clickThrough;

        // 戻すときに固定は解かない。タブを押して意図して固定したものを、勝手に捨てないため。
        if (!TryApply(next, save: true, reload: false, out var error))
            _tray?.Notify(UiText.CannotSaveSettings, error ?? UiText.UnknownCause,
                System.Windows.Forms.ToolTipIcon.Warning, onClick: () => OpenSettings(SettingsPage.Display));
    }

    /// <summary>
    /// オーバーレイのタブをクリックした。トレイからレイヤーを選んだときと同じ後始末が要る。
    /// 同じタブをもう一度押したときは固定が解ける（<see cref="OverlayController.ToggleManualLayer"/>）。
    /// </summary>
    private void OnLayerTabClicked(int layerId)
    {
        var wasEnabled = _overlay!.IsEnabled;
        _overlay.ToggleManualLayer(layerId);

        RefreshLayerHotKeys();
        _tray?.ShowState(_overlay.IsEnabled, notify: false);

        if (_overlay.IsEnabled != wasEnabled) Applied?.Invoke();
    }

    /// <summary>
    /// オーバーレイをドラッグで動かし終えた。基準位置からのずれを覚える。
    /// 整数に丸めるのは、手で読み書きする設定ファイルに長い小数を書かないため。
    /// </summary>
    private void OnOverlayDragged(double offsetX, double offsetY)
    {
        var next = _config.Clone();
        next.OffsetX = Math.Round(offsetX);
        next.OffsetY = Math.Round(offsetY);

        if (!TryApply(next, save: true, reload: false, out var error))
            _tray?.Notify(UiText.CannotSaveSettings, error ?? UiText.UnknownCause,
                System.Windows.Forms.ToolTipIcon.Warning, onClick: () => OpenSettings(SettingsPage.Display));
    }

    /// <param name="page">
    /// 開くページ。null なら、開いていればそのページのまま前に出し、新しく開くときは
    /// <see cref="DefaultSettingsPage"/> で開く。
    /// </param>
    private void OpenSettings(SettingsPage? page)
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow(this);
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();

            page ??= DefaultSettingsPage();
        }

        if (page is { } target) _settings.ShowPage(target);

        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    /// <summary>
    /// 普段は、いちばんよく触る「表示」から開く。
    /// サンプルのままか、読み込みに警告があるときは、先にキーボードを直してもらう。
    /// </summary>
    private SettingsPage DefaultSettingsPage() =>
        !_config.Zmk.IsEnabled || _warnings.Count > 0 ? SettingsPage.Keyboard : SettingsPage.Display;

    /// <param name="step">開くステップ。null なら開いているステップのまま前に出す。</param>
    /// <param name="notice">ステップの下に出しておく知らせ。</param>
    /// <param name="guideOnClose">
    /// 「完了」を見ずに閉じたら、トレイにいることを知らせる。初めて起動した人だけに使う。
    /// 既定の見せ方ではレイヤーキーを押すまで画面に何も出ず、タスクバーにも出ないので、
    /// 途中で閉じるとアプリが動いている証拠が画面から消える。
    /// </param>
    private void OpenSetup(SetupStep? step, string? notice = null, bool guideOnClose = false)
    {
        if (_setup is null)
        {
            var setup = _setup = new SetupWindow(this);

            _setup.Closed += (_, _) =>
            {
                _setup = null;

                if (guideOnClose && !setup.ReachedDone)
                    _tray?.Notify(UiText.StillRunning, UiText.StillRunningBody(_config.ToggleHotkey.ToString()),
                        onClick: () => OpenSetup(ResumeSetupStep()));
            };

            _setup.Show();
        }

        if (step is { } target) _setup.ShowStep(target);
        if (notice is not null) _setup.ShowNotice(notice);

        if (_setup.WindowState == WindowState.Minimized) _setup.WindowState = WindowState.Normal;
        _setup.Activate();
    }

    // ---- 有効・無効 ----

    /// <summary>
    /// 何も押していないときにも出す設定では、切り替えた結果が画面にそのまま出るので通知は要らない。
    /// 知らせるのは、無効にしても見た目が変わらない設定（L1 以上のときだけ、など）のときだけ。
    /// </summary>
    private void ToggleOverlay()
    {
        var enabled = _overlay!.Toggle();

        RefreshLayerHotKeys();
        _tray?.ShowState(enabled, notify: !_overlay.ShowsBaseLayer);

        // 設定画面の「いまの状態」を合わせる。開いたまま切り替えられる。
        Applied?.Invoke();
    }

    /// <summary>トレイからレイヤーを選んだ。無効だったら有効になるので、それに付随するものも合わせる。</summary>
    private void ShowLayerByHand(int layerId)
    {
        var wasEnabled = _overlay!.IsEnabled;
        _overlay.ShowManualLayer(layerId);

        RefreshLayerHotKeys();
        _tray?.ShowState(_overlay.IsEnabled, notify: false);

        // 無効だったら有効になる。変わったときだけ知らせる（レイヤーキーを叩くたびに
        // 設定画面を作り直さないように）。
        if (_overlay.IsEnabled != wasEnabled) Applied?.Invoke();
    }

    // ---- ホットキー ----

    private void RefreshHotkeys()
    {
        _toggleRegistration?.Dispose();
        _toggleRegistration = null;

        _clickThroughRegistration?.Dispose();
        _clickThroughRegistration = null;

        if (_hotkeySuspensions == 0)
        {
            RegisterToggleHotKey(_config);
            RegisterClickThroughHotKey(_config);
        }

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
        var spec = config.ToggleHotkey;
        string? error;

        // 文字の入力に使われる組み合わせを押さえると、その文字が打てなくなる。登録せずに知らせる。
        if (HotkeyRules.TypesCharacter(spec))
        {
            error = UiText.HotkeyTypesCharacter(spec.ToString());
        }
        else
        {
            _toggleRegistration = _hotKeys!.TryRegister(spec, ToggleOverlay, out error);

            if (_toggleRegistration is not null)
            {
                _toggleFailureNotified = null;
                return;
            }
        }

        var text = spec.ToString();
        if (_toggleFailureNotified == text) return;
        _toggleFailureNotified = text;

        // ここが取れないとアプリを呼び出す手段がトレイだけになるので、必ず知らせる。
        _tray?.Notify(UiText.CannotRegisterHotkey, error ?? UiText.UnknownCause,
            System.Windows.Forms.ToolTipIcon.Warning);
    }

    /// <summary>
    /// クリックの受け取りを切り替えるショートカット。
    ///
    /// レイヤー用と違って「有効なあいだだけ」では縛らない。オーバーレイの性質を変えるものなので、
    /// 動いているあいだは常に効いてよい（無効のときに押せば、固定して出てくる）。
    /// </summary>
    private void RegisterClickThroughHotKey(AppConfig config)
    {
        // 既定は割り当てなし。この PC で使える組み合わせを勝手に決めない。
        if (config.EffectiveClickThroughHotkey is not { } spec)
        {
            _clickThroughFailureNotified = null;
            return;
        }

        string? error;

        if (HotkeyRules.TypesCharacter(spec))
        {
            error = UiText.HotkeyTypesCharacter(spec.ToString());
        }
        else
        {
            _clickThroughRegistration = _hotKeys!.TryRegister(
                spec, () => SetClickThrough(!_config.ClickThrough), out error);

            if (_clickThroughRegistration is not null)
            {
                _clickThroughFailureNotified = null;
                return;
            }
        }

        var text = spec.ToString();
        if (_clickThroughFailureNotified == text) return;
        _clickThroughFailureNotified = text;

        // 利用者が自分で選んだ組み合わせなので、効かないことは知らせる。
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

            // 既定の組み合わせは ManualLayerHotkey の時点で除いてある。ここに来るのは利用者が選んだもの。
            if (HotkeyRules.TypesCharacter(spec))
            {
                failures.Add($"L{layerId}: {UiText.HotkeyTypesCharacter(spec.ToString())}");
                continue;
            }

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

    GitHubSource ISettingsHost.GitHub => _gitHub;

    bool ISettingsHost.TryApply(AppConfig config, out string? error, bool reloadKeymap) =>
        TryApply(config, save: true, reload: reloadKeymap, out error);

    bool ISettingsHost.Reload(out string? error) =>
        TryApply(_config.Clone(), save: false, reload: true, out error);

    void ISettingsHost.SetClickThrough(bool clickThrough) => SetClickThrough(clickThrough);

    /// <summary>
    /// 画面を書き出すだけのとき（--render-settings）はオーバーレイを作らないので、
    /// 既定の「有効」を返す。書き出した絵が「無効」で始まると、README の画像がそう見えてしまう。
    /// </summary>
    bool ISettingsHost.OverlayEnabled => _overlay?.IsEnabled ?? true;

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
        _clickThroughRegistration?.Dispose();
        _hotKeys?.Dispose();
        _tray?.Dispose();

        _activateWait?.Unregister(null);
        _activate?.Dispose();

        if (_instance is not null)
        {
            if (_ownsInstance)
            {
                try
                {
                    _instance.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // 別のスレッドで取ったことになっている場合。プロセスの終了で解放される。
                }
            }

            _instance.Dispose();
        }

        base.OnExit(e);
    }
}
