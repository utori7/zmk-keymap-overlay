using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;
using ZmkOverlay.App.Text;
using ZmkOverlay.Core.Config;

namespace ZmkOverlay.App.Settings;

/// <summary>
/// ショートカットの入力欄が何に割り当てるものか。レイヤーの欄は Tag にレイヤー番号（int）を持つので、
/// ここに並べるのはレイヤー以外だけ。
/// </summary>
internal enum HotkeyTarget
{
    Toggle,
    ClickThrough,
}

/// <summary>
/// 読み取り専用の <see cref="TextBox"/> をショートカットの入力欄にする。
///
/// 設定画面と初期設定の案内の両方で使う。文字の入力に使われる組み合わせの拒否・すでに使われている
/// 組み合わせの検出・Delete で外せる欄の区別は、2 か所に分かれると必ずずれるので 1 つにまとめてある。
///
/// 記録の途中は登録済みのホットキーを外す（<see cref="ISettingsHost.SuspendHotkeys"/>）。
/// 外さないと、いまの組み合わせを押しても OS に横取りされて入力欄に届かない。
/// </summary>
internal sealed class HotkeyCapture
{
    private readonly ISettingsHost _host;
    private readonly Action<Action<AppConfig>> _apply;
    private readonly Action _releaseFocus;
    private readonly List<TextBox> _boxes = new();

    private IDisposable? _suspension;

    /// <param name="apply">複製を書き換えて反映する。両ウィンドウが同じ形の Apply を持っている。</param>
    /// <param name="releaseFocus">
    /// 入力欄から抜けるときにフォーカスを渡す先。反映より先に抜けて、ホットキーの一時解除を終わらせる。
    /// </param>
    public HotkeyCapture(ISettingsHost host, Action<Action<AppConfig>> apply, Action releaseFocus)
    {
        _host = host;
        _apply = apply;
        _releaseFocus = releaseFocus;
    }

    /// <summary>入力欄として使う。</summary>
    /// <param name="target">
    /// <see cref="HotkeyTarget"/> か、レイヤーの欄ならレイヤー番号（int）。
    /// Tag は object 型で、XAML から入れると型変換が働かず文字列になるので、ここで入れる。
    /// </param>
    public void Attach(TextBox box, object target)
    {
        box.Tag = target;
        box.Text = TextFor(target);

        box.GotKeyboardFocus += OnFocus;
        box.LostKeyboardFocus += OnBlur;
        box.PreviewKeyDown += OnKeyDown;

        _boxes.Add(box);
    }

    /// <summary>作り直す欄（レイヤーの表）を捨てるときに呼ぶ。</summary>
    public void Detach(TextBox box)
    {
        box.GotKeyboardFocus -= OnFocus;
        box.LostKeyboardFocus -= OnBlur;
        box.PreviewKeyDown -= OnKeyDown;

        _boxes.Remove(box);
    }

    /// <summary>今の割り当てを欄に入れ直す。</summary>
    /// <param name="skipFocused">
    /// 記録中の欄を飛ばす。画面への流し込みで呼ぶときは true。
    /// 飛ばさないと「押してください」や衝突の理由を、操作している最中に消してしまう。
    /// </param>
    public void RefreshText(bool skipFocused = false)
    {
        foreach (var box in _boxes)
        {
            if (skipFocused && box.IsKeyboardFocused) continue;
            box.Text = TextFor(box.Tag);
        }
    }

    /// <summary>
    /// 記録をやめる。ウィンドウが非アクティブになったときと閉じるときに呼ぶこと。
    /// 呼ばないとホットキーの一時解除が残る。
    /// </summary>
    public void End()
    {
        _suspension?.Dispose();
        _suspension = null;

        RefreshText();
    }

    private string TextFor(object? target) => target switch
    {
        int layerId => _host.Config.ManualLayerHotkey(layerId)?.ToString() ?? UiText.NoShortcut,
        HotkeyTarget.ClickThrough => _host.Config.EffectiveClickThroughHotkey?.ToString() ?? UiText.NoShortcut,
        _ => _host.Config.ToggleHotkey.ToString(),
    };

    private void OnFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox box) return;

        _suspension ??= _host.SuspendHotkeys();
        box.Text = UiText.PressKeys;
    }

    private void OnBlur(object sender, KeyboardFocusChangedEventArgs e) => End();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        var layerTarget = box.Tag as int?;
        var clickThroughTarget = box.Tag is HotkeyTarget.ClickThrough;
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
            _releaseFocus();
            return;
        }

        // レイヤーとオーバーレイの操作の欄は、Delete / BackSpace で割り当てを外せる。どちらも任意の割り当て。
        // 有効 / 無効のキーは外させない。外すとアプリを呼び出す手段がトレイだけになる。
        if (Keyboard.Modifiers == ModifierKeys.None && key is Key.Delete or Key.Back)
        {
            if (layerTarget is { } clearedLayer)
            {
                _releaseFocus();
                _apply(config => config.LayerHotkeys[clearedLayer.ToString(CultureInfo.InvariantCulture)] = new HotkeySpec());
                return;
            }

            if (clickThroughTarget)
            {
                _releaseFocus();
                _apply(config => config.ClickThroughHotkey = new HotkeySpec());
                return;
            }
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

        // Shift+英字や、AltGr で記号を打つ配列での Ctrl+Alt+数字 など。押さえるとその文字が打てなくなる。
        if (HotkeyRules.TypesCharacter(spec))
        {
            box.Text = UiText.ShortcutTypesCharacter(spec.ToString());
            return;
        }

        // 同じ組み合わせを 2 か所に登録すると、後から登録したほうが黙って効かなくなる。
        // 入力欄に留まったまま理由を見せ、別の組み合わせを押してもらう。
        if (FindConflict(spec, box.Tag) is { } conflict)
        {
            box.Text = conflict;
            return;
        }

        // 先に入力欄から抜けてホットキーの一時解除を終えてから反映する。
        _releaseFocus();

        if (layerTarget is { } layerId)
            _apply(config => config.LayerHotkeys[layerId.ToString(CultureInfo.InvariantCulture)] = spec);
        else if (clickThroughTarget)
            _apply(config => config.ClickThroughHotkey = spec);
        else
            _apply(config => config.ToggleHotkey = spec);
    }

    /// <summary>その組み合わせがすでに何に使われているか。空いていれば null。</summary>
    /// <param name="target">記録しようとしている欄の Tag。自分自身との衝突は数えない。</param>
    private string? FindConflict(HotkeySpec spec, object? target)
    {
        var config = _host.Config;

        var layerTarget = target as int?;
        var isClickThrough = target is HotkeyTarget.ClickThrough;
        var isToggle = layerTarget is null && !isClickThrough;

        if (!isToggle && spec.SameAs(config.ToggleHotkey))
            return UiText.ShortcutConflict(spec.ToString(), UiText.ToggleHotkeyLabel);

        if (!isClickThrough
            && config.EffectiveClickThroughHotkey is { } clickThrough
            && spec.SameAs(clickThrough))
            return UiText.ShortcutConflict(spec.ToString(), UiText.ClickThroughHotkeyLabel);

        foreach (var layer in _host.Keymap.Layers)
        {
            if (layer.Index != layerTarget
                && config.ManualLayerHotkey(layer.Index) is { } other
                && spec.SameAs(other))
                return UiText.ShortcutConflict(spec.ToString(), UiText.LayerShortcutUse(layer.Index));

            // 合図キーは修飾キーの組み合わせすべてで押さえているので、修飾つきでも使えない。
            if (config.LayerSync.Enabled
                && layer.SignalKey is { } signal
                && string.Equals(spec.Key.Trim(), signal.Trim(), StringComparison.OrdinalIgnoreCase))
                return UiText.ShortcutConflict(spec.ToString(), UiText.SignalKeyUse(layer.Index));
        }

        return null;
    }
}
