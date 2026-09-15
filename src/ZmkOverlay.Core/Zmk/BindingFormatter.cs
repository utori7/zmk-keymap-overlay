using ZmkOverlay.Core.Dts;
using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Text;

namespace ZmkOverlay.Core.Zmk;

/// <summary>
/// バインディングを表示ラベルにする。
///
/// 中央（tap）と左上（hold）の 2 段で描くため、hold-tap 系は
/// どちらが hold でどちらが tap かを判定する必要がある。組み込みの
/// <c>&amp;lt</c> / <c>&amp;mt</c> は既知だが、キーマップ側で定義された
/// hold-tap は <c>bindings</c> と <c>#binding-cells</c> から解く。
/// </summary>
public sealed class BindingFormatter
{
    /// <summary>組み込みビヘイビアが取る引数の数。custom は定義から読む。</summary>
    private static readonly Dictionary<string, int> BuiltinCells = new(StringComparer.Ordinal)
    {
        ["kp"] = 1, ["mo"] = 1, ["to"] = 1, ["tog"] = 1, ["sl"] = 1,
        ["lt"] = 2, ["mt"] = 2,
        ["trans"] = 0, ["none"] = 0,
        ["bootloader"] = 0, ["sys_reset"] = 0, ["reset"] = 0,
        ["caps_word"] = 0, ["key_repeat"] = 0, ["studio_unlock"] = 0,
        ["mkp"] = 1, ["msc"] = 1, ["bt"] = 2, ["out"] = 1,
        ["macro_press"] = 0, ["macro_release"] = 0, ["macro_tap"] = 0,
        ["macro_pause_for_release"] = 0,
    };

    // 表示言語で変わるラベルは (日本語, 英語) で持ち、引くときに選ぶ。
    private static readonly Dictionary<string, (string Ja, string En)> MouseButtons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MB1"] = ("M左", "LMB"), ["LCLK"] = ("M左", "LMB"),
        ["MB2"] = ("M右", "RMB"), ["RCLK"] = ("M右", "RMB"),
        ["MB3"] = ("M中", "MMB"), ["MCLK"] = ("M中", "MMB"),
        ["MB4"] = ("M4", "M4"), ["MB5"] = ("M5", "M5"),
    };

    private static readonly Dictionary<string, (string Ja, string En)> BluetoothActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BT_CLR"] = ("BT消", "BT Clr"),
        ["BT_CLR_ALL"] = ("BT全消", "BT ClrAll"),
        ["BT_NXT"] = ("BT次", "BT Next"),
        ["BT_PRV"] = ("BT前", "BT Prev"),
        ["BT_DISC"] = ("BT切断", "BT Disc"),
    };

    private readonly IReadOnlyDictionary<string, BehaviorInfo> _behaviors;
    private readonly IReadOnlyDictionary<int, string> _layerNames;
    private readonly HostLayout _layout;
    private readonly IReadOnlyDictionary<string, string>? _overrides;

    public BindingFormatter(
        IReadOnlyDictionary<string, BehaviorInfo> behaviors,
        IReadOnlyDictionary<int, string> layerNames,
        HostLayout layout,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        _behaviors = behaviors;
        _layerNames = layerNames;
        _layout = layout;
        _overrides = overrides;
    }

    public KeyLabel Format(ZmkBinding binding) => Format(binding, depth: 0);

    private KeyLabel Format(ZmkBinding binding, int depth)
    {
        // 自己参照するビヘイビア定義があっても止まるようにする。
        if (depth > 8) return new KeyLabel { Tap = binding.Behavior };

        switch (binding.Behavior)
        {
            case "trans":
                return KeyLabel.Trans();

            case "none":
                return KeyLabel.None();

            case "kp":
                return Keycode(binding.Param(0) ?? "");

            case "mo":
                return new KeyLabel { Tap = LayerName(binding.Param(0)), Kind = KeyKind.Layer };

            case "to":
                return new KeyLabel { Tap = "→" + LayerName(binding.Param(0)), Kind = KeyKind.Layer };

            case "tog":
                return new KeyLabel { Tap = "⇄" + LayerName(binding.Param(0)), Kind = KeyKind.Layer };

            case "sl":
                return new KeyLabel { Tap = "◇" + LayerName(binding.Param(0)), Kind = KeyKind.Layer };

            case "lt":
                return new KeyLabel
                {
                    Hold = LayerName(binding.Param(0)),
                    Tap = Keycode(binding.Param(1) ?? "").Tap,
                    Kind = KeyKind.Layer,
                };

            case "mt":
                return new KeyLabel
                {
                    Hold = Keycode(binding.Param(0) ?? "").Tap,
                    Tap = Keycode(binding.Param(1) ?? "").Tap,
                    Kind = KeyKind.Modifier,
                };

            case "mkp":
                return new KeyLabel
                {
                    Tap = Lookup(MouseButtons, binding.Param(0)),
                    Kind = KeyKind.System,
                };

            case "msc":
                return new KeyLabel { Tap = Strings.T("スクロール", "Scroll"), Kind = KeyKind.System };

            case "bt":
                return new KeyLabel { Tap = Bluetooth(binding), Kind = KeyKind.System };

            case "out":
                return new KeyLabel { Tap = Strings.T("出力", "Output"), Kind = KeyKind.System };

            case "bootloader":
                return new KeyLabel { Tap = "BOOT", Kind = KeyKind.System };

            case "sys_reset" or "reset":
                return new KeyLabel { Tap = "RESET", Kind = KeyKind.System };

            case "caps_word":
                return new KeyLabel { Tap = "CapsWd", Kind = KeyKind.Modifier };

            case "key_repeat":
                return new KeyLabel { Tap = "Repeat" };

            case "studio_unlock":
                return new KeyLabel { Tap = "Studio", Kind = KeyKind.System };
        }

        if (_behaviors.TryGetValue(binding.Behavior, out var info)) return Custom(binding, info, depth);

        // 知らないビヘイビアは名前を出す。表示が壊れるより気づけるほうがよい。
        return new KeyLabel { Tap = binding.Behavior };
    }

    private KeyLabel Custom(ZmkBinding binding, BehaviorInfo info, int depth)
    {
        if (info.IsHoldTap && info.Bindings.Count >= 2)
        {
            var holdRef = info.Bindings[0].Behavior;
            var tapRef = info.Bindings[1].Behavior;

            var holdCells = CellCount(holdRef);
            var holdParams = binding.Parameters.Take(holdCells).ToList();
            var tapParams = binding.Parameters.Skip(holdCells).ToList();

            var hold = Format(new ZmkBinding(holdRef, holdParams), depth + 1);
            var tap = Format(new ZmkBinding(tapRef, tapParams), depth + 1);

            return new KeyLabel
            {
                Hold = hold.Tap,
                Tap = tap.Tap,
                Kind = hold.Kind == KeyKind.Layer ? KeyKind.Layer
                     : hold.Kind == KeyKind.Modifier ? KeyKind.Modifier
                     : tap.Kind,
            };
        }

        if (info.IsMacro)
        {
            // マクロそのものに表示名は無い。中で何をしているかを見て、
            // レイヤー切替を含むならそれを名乗らせる（合図キー付きの
            // レイヤー切替マクロがこれに当たる）。
            foreach (var inner in info.Bindings)
            {
                if (inner.Behavior is "mo" or "to" or "tog" or "sl")
                    return new KeyLabel
                    {
                        Tap = LayerName(ResolvePlaceholder(inner.Param(0), binding)),
                        Kind = KeyKind.Layer,
                    };
            }

            foreach (var inner in info.Bindings)
            {
                if (inner.Behavior == "kp" && inner.Param(0) is { } code)
                    return Keycode(ResolvePlaceholder(code, binding) ?? code);
            }
        }

        return new KeyLabel { Tap = info.Label };
    }

    /// <summary>
    /// 引数つきマクロの本体は <c>MACRO_PLACEHOLDER</c> を持つ。これは
    /// <c>behaviors.dtsi</c> の定義で、こちらはシステムヘッダを読まないので
    /// 展開されずに残る。呼び出し側の実引数で埋めてやる。
    /// </summary>
    private static string? ResolvePlaceholder(string? value, ZmkBinding invocation)
    {
        if (value is null) return null;
        if (DtsValue.TryParseNumber(value, out _)) return value;

        return invocation.Param(0) ?? value;
    }

    private int CellCount(string behavior)
    {
        if (_behaviors.TryGetValue(behavior, out var info) && info.BindingCells is { } cells) return cells;

        return BuiltinCells.TryGetValue(behavior, out var builtin) ? builtin : 1;
    }

    private KeyLabel Keycode(string code)
    {
        var text = KeycodeTable.Label(code, _layout, _overrides);

        return new KeyLabel
        {
            Tap = text,
            Kind = KeycodeTable.IsModifier(code) ? KeyKind.Modifier
                 : text.Length > 1 && (text.StartsWith('^') || text.StartsWith("Alt") ||
                                       text.StartsWith("Win") || text.StartsWith('⇧'))
                     ? KeyKind.Modifier
                     : KeyKind.Normal,
        };
    }

    private string Bluetooth(ZmkBinding binding)
    {
        var action = binding.Param(0) ?? "";

        if (action.Equals("BT_SEL", StringComparison.OrdinalIgnoreCase))
            return "BT" + (binding.Param(1) ?? "");

        return Lookup(BluetoothActions, action);
    }

    private string LayerName(string? parameter)
    {
        if (parameter is null) return "L?";

        if (!DtsValue.TryParseNumber(parameter, out var index))
            return parameter;   // 展開されなかったマクロ名など。そのまま見せる。

        return _layerNames.TryGetValue(index, out var name) && name.Length > 0
            ? $"L{index} {name}"
            : $"L{index}";
    }

    private static string Lookup(IReadOnlyDictionary<string, (string Ja, string En)> table, string? key)
        => key is not null && table.TryGetValue(key, out var value) ? Strings.T(value.Ja, value.En) : key ?? "";
}
