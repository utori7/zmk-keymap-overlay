using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;
using ZmkOverlay.App.Overlay;
using ZmkOverlay.App.Text;
using ZmkOverlay.Core.Config;

namespace ZmkOverlay.App.Interop;

/// <summary>
/// キーボードのレイヤーにオーバーレイを追従させる。
///
/// ZMK 側でレイヤーに入るとき合図キー（F13〜）を押しっぱなしにしておき、
/// こちらは RegisterHotKey でそれを受ける。押されたことは WM_HOTKEY で分かるが
/// 離されたことは通知されないので、GetAsyncKeyState を定期的に見に行く。
///
/// この 2 つが両立することは実測済み。DESIGN.md「V1 の検証結果」を参照。
/// 全キーを監視する低レベルフックは使っていない。
/// </summary>
internal sealed class LayerSyncService : IDisposable
{
    private readonly HotKeyService _hotKeys;
    private readonly OverlayController _overlay;
    private readonly LayerSyncConfig _config;
    private readonly DispatcherTimer _timer;
    private readonly List<IDisposable> _registrations = new();

    /// <summary>いま押されているとみなしている合図キー。0 なら追従していない。</summary>
    private int _activeVk;

    private bool _downObserved;
    private readonly Stopwatch _sinceTrigger = new();

    private bool _disposed;

    /// <summary>合図キーを受け取った。設定画面の「テスト」表示に使う。</summary>
    public event Action<int>? SignalReceived;

    public LayerSyncService(
        HotKeyService hotKeys, OverlayController overlay, LayerSyncConfig config)
    {
        _hotKeys = hotKeys;
        _overlay = overlay;
        _config = config;

        _timer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, config.PollIntervalMs)),
        };
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// 合図キーを持つレイヤーぶんだけ登録する。
    /// 登録できなかったキーは戻り値で返す（呼び出し側が利用者に知らせる）。
    /// </summary>
    public IReadOnlyList<string> Start()
    {
        var failures = new List<string>();
        if (!_config.Enabled) return failures;

        foreach (var (layerId, signalKey) in _overlay.SignalKeys)
        {
            if (!HotKeyService.TryParseVirtualKey(signalKey, out var vk))
            {
                failures.Add(UiText.SignalKeyUnrecognized(layerId, signalKey));
                continue;
            }

            // 修飾キーなしの単独キーとして予約する。これで他アプリにも届かなくなる。
            var spec = new HotkeySpec { Key = signalKey };
            var id = layerId;

            var registration = _hotKeys.TryRegister(spec, () => OnSignal(id, vk), out var error);

            if (registration is null) failures.Add($"L{layerId} ({signalKey}): {error}");
            else _registrations.Add(registration);
        }

        return failures;
    }

    public void Stop()
    {
        _timer.Stop();
        _activeVk = 0;

        foreach (var registration in _registrations) registration.Dispose();
        _registrations.Clear();
    }

    private void OnSignal(int layerId, int vk)
    {
        SignalReceived?.Invoke(layerId);

        if (!_config.IsHoldMode)
        {
            if (_overlay.IsShowingLayer(layerId)) _overlay.EndTransient();
            else _overlay.BeginTransient(layerId);
            return;
        }

        _activeVk = vk;
        _downObserved = false;
        _sinceTrigger.Restart();

        _overlay.BeginTransient(layerId);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_activeVk == 0)
        {
            _timer.Stop();
            return;
        }

        if (NativeMethods.IsKeyDown(_activeVk))
        {
            _downObserved = true;
            return;
        }

        // 押下を一度も見ていないうちは、取りこぼしと区別できない。
        // 猶予のあいだは待ち、それを過ぎたら離されたものとして畳む。
        if (!_downObserved && _sinceTrigger.ElapsedMilliseconds < _config.GraceMs) return;

        _timer.Stop();
        _activeVk = 0;
        _overlay.EndTransient();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Tick -= OnTick;
        Stop();
    }
}
