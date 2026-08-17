using System;
using System.Collections.Generic;
using System.Windows;
using ZmkOverlay.App.Interop;
using ZmkOverlay.App.Overlay;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;

namespace ZmkOverlay.App;

public partial class App : Application
{
    private HotKeyService? _hotKeys;
    private OverlayController? _overlay;
    private LayerSyncService? _layerSync;
    private TrayIcon? _tray;

    private IDisposable? _toggleRegistration;
    private readonly List<IDisposable> _layerRegistrations = new();

    private string _configPath = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            ShowError("予期しないエラー", args.Exception);
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
            if (TryRunStartupMode(e.Args)) return;

            Initialize();
        }
        catch (Exception ex)
        {
            ShowError("起動できませんでした", ex);
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
        var (config, layout, keymap) = LoadAll();

        for (var slot = 0; slot < keymap.Layers.Count; slot++)
        {
            var layer = keymap.Layers[slot];
            var file = System.IO.Path.Combine(outputDir, $"layer{layer.Index}.png");

            Render.OffscreenRenderer.RenderToPng(config, layout, keymap, slot, file);
            Console.WriteLine(file);
        }

        Shutdown();
        return true;
    }

    private AppConfig? _config;

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
        var (config, layout, keymap) = LoadAll();

        _config = config;
        _hotKeys = new HotKeyService();
        _overlay = new OverlayController(config, layout, keymap);

        _tray = new TrayIcon(
            toggle: ToggleOverlay,
            reload: Reload,
            exit: () => Shutdown(),
            alwaysVisible: config.IsAlwaysVisible,
            setAlwaysVisible: SetAlwaysVisible,
            runAtLogin: StartupEntry.IsEnabled,
            setRunAtLogin: SetRunAtLogin);

        RegisterToggleHotKey(config);
        StartLayerSync(config);

        _overlay.Start();
        ApplyEnabledState(_overlay.IsEnabled, notify: false);

        ReportWarnings();
    }

    private void ToggleOverlay() => ApplyEnabledState(_overlay!.Toggle(), notify: true);

    /// <summary>
    /// 有効・無効に付随するものをまとめて合わせる。
    ///
    /// レイヤー直接指定のホットキーは、表示中ではなく「有効なあいだ」押さえる。
    /// 表示に連動させると、L1 以上のときだけ表示する設定では
    /// 素の状態で押さえておらず使えないうえ、レイヤーキーを叩くたびに
    /// 登録と解除を繰り返すことになる。
    /// </summary>
    private void ApplyEnabledState(bool enabled, bool notify)
    {
        if (enabled && _config is not null) RegisterLayerHotKeys(_config);
        else ReleaseLayerHotKeys();

        _tray?.ShowState(enabled, notify);
    }

    private string? _configOverride;

    private (AppConfig, PhysicalLayout, Keymap) LoadAll()
    {
        var path = _configOverride ?? ConfigPaths.FindConfig();

        AppConfig config;
        if (path is null)
        {
            // 初回起動。既定値を書き出して、以後は利用者が編集できるようにする。
            config = new AppConfig();
            path = ConfigPaths.DefaultWriteTarget();
            config.Save(path);
        }
        else
        {
            config = AppConfig.Load(path);
        }

        _configPath = path;

        var loaded = KeymapLoader.Load(config, path);

        // ZMK のソースを読むと、解釈できなかった箇所が警告として返る。
        // 黙って落とすと「なぜこのキーだけ変なのか」が分からなくなるので出す。
        _pendingWarnings = loaded.Warnings;

        return (config, loaded.Layout, loaded.Keymap);
    }

    private IReadOnlyList<string> _pendingWarnings = Array.Empty<string>();

    private void ReportWarnings()
    {
        if (_pendingWarnings.Count == 0) return;

        _tray?.Notify(
            $"キーマップの読み込みで {_pendingWarnings.Count} 件の警告",
            string.Join("\n", _pendingWarnings.Take(5)),
            System.Windows.Forms.ToolTipIcon.Warning);

        _pendingWarnings = Array.Empty<string>();
    }

    private void Reload()
    {
        try
        {
            var (config, layout, keymap) = LoadAll();

            _config = config;
            _overlay!.Reload(config, layout, keymap);
            _tray?.SyncAlwaysVisible(config.IsAlwaysVisible);

            _toggleRegistration?.Dispose();
            RegisterToggleHotKey(config);

            _layerSync?.Dispose();
            StartLayerSync(config);

            ApplyEnabledState(_overlay.IsEnabled, notify: false);

            if (_pendingWarnings.Count > 0) ReportWarnings();
            else _tray?.Notify("再読み込みしました", _configPath);
        }
        catch (Exception ex)
        {
            ShowError("再読み込みに失敗しました", ex);
        }
    }

    /// <summary>
    /// トレイから切り替えたら設定ファイルにも書き戻す。次回起動で戻ってしまうと
    /// 「設定したのに効いていない」と見えるため。
    /// </summary>
    private void SetAlwaysVisible(bool always)
    {
        if (_config is null) return;

        _config.DisplayMode = always ? "always" : "layersOnly";
        _overlay?.ApplyConfig(_config);

        try
        {
            _config.Save(_configPath);
        }
        catch (Exception ex)
        {
            // 保存できなくても今の動作は変えたままにする。次回起動で戻るだけ。
            _tray?.Notify("設定を保存できません", ex.Message,
                System.Windows.Forms.ToolTipIcon.Warning);
        }
    }

    /// <summary>
    /// 自動起動の登録・解除。設定ファイルではなくスタートアップフォルダの
    /// 実体が唯一の情報源なので、失敗したらメニューの表示を実体に戻す。
    /// </summary>
    private void SetRunAtLogin(bool enable)
    {
        try
        {
            // --config で起動していたときだけ、その指定を引き継ぐ。
            if (enable) StartupEntry.Enable(_configOverride);
            else StartupEntry.Disable();

            _tray?.Notify(
                enable ? "サインイン時に起動します" : "サインイン時の起動をやめました",
                StartupEntry.FolderPath);
        }
        catch (Exception ex)
        {
            _tray?.Notify("自動起動を設定できません", ex.Message,
                System.Windows.Forms.ToolTipIcon.Warning);
        }
        finally
        {
            _tray?.SyncRunAtLogin(StartupEntry.IsEnabled);
        }
    }

    private void RegisterToggleHotKey(AppConfig config)
    {
        _toggleRegistration = _hotKeys!.TryRegister(
            config.ToggleHotkey, ToggleOverlay, out var error);

        if (_toggleRegistration is null)
        {
            // ここが取れないとアプリを呼び出す手段がトレイだけになるので、必ず知らせる。
            _tray?.Notify("ホットキーを登録できません", error ?? "原因不明",
                System.Windows.Forms.ToolTipIcon.Warning);
        }
    }

    private void StartLayerSync(AppConfig config)
    {
        _layerSync = new LayerSyncService(_hotKeys!, _overlay!, config.LayerSync);

        var failures = _layerSync.Start();
        if (failures.Count == 0) return;

        // 合図キーが取れないレイヤーは黙って追従しなくなるだけなので、
        // 気づけるように知らせる。他のレイヤーは動き続ける。
        _tray?.Notify("追従できないレイヤーがあります", string.Join("\n", failures),
            System.Windows.Forms.ToolTipIcon.Warning);
    }

    private void RegisterLayerHotKeys(AppConfig config)
    {
        if (!config.EnableManualLayerKeys) return;

        ReleaseLayerHotKeys();

        foreach (var layerId in _overlay!.LayerIds)
        {
            if (layerId is < 0 or > 9) continue;

            var spec = new HotkeySpec
            {
                Modifiers = { "Ctrl", "Alt" },
                Key = layerId.ToString(),
            };

            var id = layerId;
            var registration = _hotKeys!.TryRegister(spec, () => _overlay!.ShowManualLayer(id), out _);

            // 取れない番号があっても他は動かしたいので、失敗は黙って飛ばす。
            if (registration is not null) _layerRegistrations.Add(registration);
        }
    }

    private void ReleaseLayerHotKeys()
    {
        foreach (var registration in _layerRegistrations) registration.Dispose();
        _layerRegistrations.Clear();
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
            $"{ex.Message}{Environment.NewLine}{Environment.NewLine}詳細: {logPath}",
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
