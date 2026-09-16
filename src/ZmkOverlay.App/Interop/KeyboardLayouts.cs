using System;
using System.Linq;
using System.Text;
using ZmkOverlay.Core.Config;

namespace ZmkOverlay.App.Interop;

/// <summary>
/// この PC に入っているキーボード配列から分かること。
///
/// Ctrl+Alt は多くの欧州配列で AltGr と同じ扱いになる（ドイツ語配列の Ctrl+Alt+7 は「{」）。
/// そういう組み合わせをホットキーとして押さえると、その文字が打てなくなるので、使う前に確かめる。
/// </summary>
internal static class KeyboardLayouts
{
    /// <summary>
    /// その組み合わせを押すと、入っているいずれかの配列で文字が入力されるか。
    /// 調べるのは Ctrl+Alt を含むもの（AltGr として）と、修飾なしか Shift だけのもの。
    /// Win を含むもの、Ctrl だけ・Alt だけのものは文字を出さない。
    /// </summary>
    public static bool TypesCharacter(HotkeySpec spec)
    {
        if (spec.Has("win")) return false;

        var ctrl = spec.Has("ctrl");
        var alt = spec.Has("alt");
        if (ctrl != alt) return false;

        if (!HotKeyService.TryParseVirtualKey(spec.Key, out var vk)) return false;

        var state = new byte[256];

        if (ctrl && alt)
        {
            // AltGr は左 Ctrl + 右 Alt として届く。
            state[NativeMethods.VK_CONTROL] = 0x80;
            state[NativeMethods.VK_LCONTROL] = 0x80;
            state[NativeMethods.VK_MENU] = 0x80;
            state[NativeMethods.VK_RMENU] = 0x80;
        }

        if (spec.Has("shift"))
        {
            state[NativeMethods.VK_SHIFT] = 0x80;
            state[NativeMethods.VK_LSHIFT] = 0x80;
        }

        foreach (var layout in InstalledLayouts())
        {
            var scan = NativeMethods.MapVirtualKeyEx((uint)vk, NativeMethods.MAPVK_VK_TO_VSC, layout);
            var buffer = new StringBuilder(8);

            var result = NativeMethods.ToUnicodeEx(
                (uint)vk, scan, state, buffer, buffer.Capacity, NativeMethods.TOUNICODE_NO_STATE_CHANGE, layout);

            // 負の値はデッドキー（次の文字と組み合わさる記号）。これも文字の入力に使われている。
            if (result < 0) return true;
            if (result > 0 && buffer.ToString(0, Math.Min(result, buffer.Length)).Any(c => !char.IsControl(c))) return true;
        }

        return false;
    }

    /// <summary>
    /// PC のキーボードが日本語配列なら "jis"、それ以外は "us"。初めて起動したときの既定値に使う。
    /// 表示言語ではなくキーボードの種類で決める（日本語の Windows に英語配列のキーボード、という人もいる）。
    /// </summary>
    public static string DetectHostLayout() =>
        NativeMethods.GetKeyboardType(0) == NativeMethods.KEYBOARD_TYPE_JAPANESE ? "jis" : "us";

    private static IntPtr[] InstalledLayouts()
    {
        var count = NativeMethods.GetKeyboardLayoutList(0, null);
        if (count <= 0) return new[] { NativeMethods.GetKeyboardLayout(0) };

        var layouts = new IntPtr[count];
        var filled = NativeMethods.GetKeyboardLayoutList(count, layouts);

        return filled > 0 ? layouts.Take(filled).ToArray() : new[] { NativeMethods.GetKeyboardLayout(0) };
    }
}
