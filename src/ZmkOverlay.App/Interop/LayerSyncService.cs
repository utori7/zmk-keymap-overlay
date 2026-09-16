using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    /// <summary>
    /// 押されているとみなしている合図キー。重ねたレイヤー（L1 を押したまま L3 に入る、など）では複数になる。
    ///
    /// 1 つだけ覚える作りだと、L1 を押したまま L3 に入って L3 のキーを離したとき、
    /// L1 のキーはまだ押されているのにオーバーレイが消えてしまう。
    /// </summary>
    private readonly List<HeldSignal> _held = new();

    /// <summary>いま表示しているレイヤー。何も押されていなければ null。</summary>
    private int? _shown;

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

            // 単独のキーとして予約する。これで他アプリにも届かなくなる。
            // 修飾キーを押したままレイヤーに入っても受け取れるよう、修飾つきでも押さえる。
            var id = layerId;
            var registration = _hotKeys.TryRegisterAnyModifiers(signalKey, () => OnSignal(id, vk), out var error);

            if (registration is null) failures.Add($"L{layerId} ({signalKey}): {error}");
            else _registrations.Add(registration);
        }

        return failures;
    }

    public void Stop()
    {
        _timer.Stop();
        _held.Clear();
        _shown = null;

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

        _held.RemoveAll(h => h.Vk == vk);
        _held.Add(new HeldSignal(layerId, vk));

        // 押された瞬間は、同じレイヤーでも表示し直す（無効から戻した直後など、隠れていることがある）。
        _shown = ShownLayer();
        _overlay.BeginTransient(_shown.Value);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_held.Count == 0)
        {
            _timer.Stop();
            return;
        }

        var anyReleased = false;

        foreach (var held in _held.ToList())
        {
            if (NativeMethods.IsKeyDown(held.Vk))
            {
                held.DownObserved = true;
                continue;
            }

            // 押下を一度も見ていないうちは、取りこぼしと区別できない。
            // 猶予のあいだは待ち、それを過ぎたら離されたものとして畳む。
            if (!held.DownObserved && held.SinceTrigger.ElapsedMilliseconds < _config.GraceMs) continue;

            _held.Remove(held);
            anyReleased = true;
        }

        if (!anyReleased) return;

        if (_held.Count == 0)
        {
            _timer.Stop();
            _shown = null;
            _overlay.EndTransient();
            return;
        }

        // 上に重ねたレイヤーを離しても、下のレイヤーのキーはまだ押されている。そちらの表示に戻す。
        Show(ShownLayer());
    }

    private void Show(int layerId)
    {
        if (_shown == layerId) return;

        _shown = layerId;
        _overlay.BeginTransient(layerId);
    }

    /// <summary>
    /// 表示するレイヤー。押されている合図キーのレイヤーに、その組み合わせで入る条件付きレイヤーを足し、
    /// その中で番号が最も大きいもの。ZMK も番号の大きいレイヤーからキーを探すので、実際に効いている配置と一致する。
    /// Lower と Raise を同時に押したときは、条件付きレイヤーの Adjust が出る。
    /// </summary>
    private int ShownLayer()
    {
        var keymap = _overlay.Keymap;
        var active = keymap.WithConditionalLayers(_held.Select(h => h.LayerId));
        var shown = active.Where(id => keymap.Layers.Any(l => l.Index == id)).ToList();

        return shown.Count > 0 ? shown.Max() : _held[^1].LayerId;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Tick -= OnTick;
        Stop();
    }

    private sealed class HeldSignal
    {
        public HeldSignal(int layerId, int vk)
        {
            LayerId = layerId;
            Vk = vk;
        }

        public int LayerId { get; }

        public int Vk { get; }

        public bool DownObserved { get; set; }

        public Stopwatch SinceTrigger { get; } = Stopwatch.StartNew();
    }
}
