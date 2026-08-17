using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;
using ZmkOverlay.Core.Config;

namespace ZmkOverlay.App.Interop;

/// <summary>
/// グローバルホットキーの登録先。
///
/// 低レベルキーボードフック（WH_KEYBOARD_LL）ではなく RegisterHotKey を使う。
/// フックは全キー入力を監視するため EDR にキーロガーとして検知されうる一方、
/// RegisterHotKey は「特定キーを OS に予約する」公式 API で、
/// 登録したキーは他アプリにも届かなくなる。Phase 2 の合図キー検知でもこの性質を使う。
/// 詳細は DESIGN.md「なぜ低レベルフックを使わないか」。
/// </summary>
public sealed class HotKeyService : IDisposable
{
    private const int HwndMessage = -3;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;
    private bool _disposed;

    public HotKeyService()
    {
        // メッセージ専用ウィンドウ。表示を持たないので、オーバーレイの表示状態と
        // ホットキーの生存期間を切り離せる。
        var parameters = new HwndSourceParameters("ZmkOverlayHotKeys")
        {
            ParentWindow = new IntPtr(HwndMessage),
            Width = 0,
            Height = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    /// <summary>
    /// 登録に成功したら解除用のトークンを返す。失敗したら null と理由を返す。
    /// 他アプリが同じ組み合わせを先に取っていると失敗するので、呼び出し側で通知すること。
    /// </summary>
    public IDisposable? TryRegister(HotkeySpec spec, Action onPressed, out string? error)
    {
        error = null;

        if (!TryParseVirtualKey(spec.Key, out var vk))
        {
            error = $"キー '{spec.Key}' を解釈できません。";
            return null;
        }

        var modifiers = MOD_NOREPEAT_BASE;
        foreach (var m in spec.Modifiers)
        {
            switch (m.Trim().ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= NativeMethods.MOD_CONTROL; break;
                case "alt": modifiers |= NativeMethods.MOD_ALT; break;
                case "shift": modifiers |= NativeMethods.MOD_SHIFT; break;
                case "win" or "windows": modifiers |= NativeMethods.MOD_WIN; break;
                default:
                    error = $"修飾キー '{m}' を解釈できません。";
                    return null;
            }
        }

        var id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers, (uint)vk))
        {
            error = $"{spec} は登録できませんでした。他のアプリが使用している可能性があります。";
            return null;
        }

        _handlers[id] = onPressed;
        return new Registration(this, id);
    }

    private const uint MOD_NOREPEAT_BASE = NativeMethods.MOD_NOREPEAT;

    private void Unregister(int id)
    {
        if (_handlers.Remove(id))
            NativeMethods.UnregisterHotKey(_source.Handle, id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_HOTKEY) return IntPtr.Zero;

        if (_handlers.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// "K" / "F13" / "1" / "Right" を仮想キーコードに変換する。
    /// 数字は WPF の Key 列挙では D1..D0 なので補正する。
    /// </summary>
    public static bool TryParseVirtualKey(string name, out int vk)
    {
        vk = 0;
        if (string.IsNullOrWhiteSpace(name)) return false;

        var normalized = name.Trim();
        if (normalized.Length == 1 && char.IsDigit(normalized[0]))
            normalized = "D" + normalized;

        if (!Enum.TryParse<Key>(normalized, ignoreCase: true, out var key)) return false;
        if (key == Key.None) return false;

        vk = KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var id in new List<int>(_handlers.Keys))
            NativeMethods.UnregisterHotKey(_source.Handle, id);

        _handlers.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private sealed class Registration : IDisposable
    {
        private readonly HotKeyService _owner;
        private readonly int _id;
        private bool _released;

        public Registration(HotKeyService owner, int id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            _owner.Unregister(_id);
        }
    }
}
