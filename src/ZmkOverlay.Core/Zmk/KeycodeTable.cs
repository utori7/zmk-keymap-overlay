using ZmkOverlay.Core.Text;

namespace ZmkOverlay.Core.Zmk;

public enum HostLayout
{
    Us,
    Jis,
}

/// <summary>
/// ZMK のキーコード名を表示ラベルに直す。
///
/// ZMK が送るのは US 位置の HID コードで、実際に出る文字は
/// OS 側のキーボード配列で決まる。つまり同じ <c>&amp;kp LBKT</c> でも
/// US なら <c>[</c>、JIS なら <c>@</c> になる。ここはその差を持つ。
///
/// ZMK の <c>dt-bindings/zmk/keys.h</c> は手元に無い前提なので、
/// 使う範囲を内蔵表として持つ。知らない名前はそのまま表示して、
/// 表に無いことが画面から分かるようにしてある。
/// </summary>
public static class KeycodeTable
{
    /// <summary>文字を出すキー 1 個。配列ごとに素の面とシフト面を持つ。</summary>
    private sealed record Face(string UsBase, string UsShift, string JisBase, string JisShift);

    private static readonly Dictionary<string, Face> Faces = new(StringComparer.OrdinalIgnoreCase)
    {
        // 数字列。JIS はシフト面が US と大きく違う。
        ["N1"] = new("1", "!", "1", "!"),
        ["N2"] = new("2", "@", "2", "\""),
        ["N3"] = new("3", "#", "3", "#"),
        ["N4"] = new("4", "$", "4", "$"),
        ["N5"] = new("5", "%", "5", "%"),
        ["N6"] = new("6", "^", "6", "&"),
        ["N7"] = new("7", "&", "7", "'"),
        ["N8"] = new("8", "*", "8", "("),
        ["N9"] = new("9", "(", "9", ")"),
        ["N0"] = new("0", ")", "0", ""),

        ["MINUS"] = new("-", "_", "-", "="),
        ["EQUAL"] = new("=", "+", "^", "~"),
        ["LBKT"] = new("[", "{", "@", "`"),
        ["RBKT"] = new("]", "}", "[", "{"),
        ["BSLH"] = new("\\", "|", "]", "}"),
        ["SEMI"] = new(";", ":", ";", "+"),
        ["SQT"] = new("'", "\"", ":", "*"),
        // JIS 面は JisKeyNames が先に拾う。
        ["GRAVE"] = new("`", "~", "", ""),
        ["COMMA"] = new(",", "<", ",", "<"),
        ["DOT"] = new(".", ">", ".", ">"),
        ["SLASH"] = new("/", "?", "/", "?"),

        // JIS 固有キー。US 配列では何も出ない。
        ["INT1"] = new("", "", "\\", "_"),
        ["INT3"] = new("", "", "¥", "|"),
    };

    /// <summary>
    /// JIS 配列で、文字ではなく名前を出すキー。名前は表示言語で変える。
    /// シフトしても同じ名前を出す。
    /// </summary>
    private static readonly Dictionary<string, (string Ja, string En)> JisKeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GRAVE"] = ("半/全", "Zen/Han"),
        ["INT2"] = ("かな", "Kana"),
        ["INT4"] = ("変換", "Henkan"),
        ["INT5"] = ("無変換", "Muhenkan"),
    };

    /// <summary>同じキーを指す別名。ZMK は 1 つのキーに複数の名前を持つ。</summary>
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NUMBER_1"] = "N1", ["NUMBER_2"] = "N2", ["NUMBER_3"] = "N3",
        ["NUMBER_4"] = "N4", ["NUMBER_5"] = "N5", ["NUMBER_6"] = "N6",
        ["NUMBER_7"] = "N7", ["NUMBER_8"] = "N8", ["NUMBER_9"] = "N9",
        ["NUMBER_0"] = "N0",

        ["LEFT_BRACKET"] = "LBKT", ["RIGHT_BRACKET"] = "RBKT",
        ["BACKSLASH"] = "BSLH", ["SEMICOLON"] = "SEMI",
        ["APOS"] = "SQT", ["APOSTROPHE"] = "SQT", ["SINGLE_QUOTE"] = "SQT",
        ["PERIOD"] = "DOT", ["FSLH"] = "SLASH", ["FORWARD_SLASH"] = "SLASH",

        ["INTERNATIONAL_1"] = "INT1", ["INTERNATIONAL_2"] = "INT2",
        ["INTERNATIONAL_3"] = "INT3", ["INTERNATIONAL_4"] = "INT4",
        ["INTERNATIONAL_5"] = "INT5",
        ["INT_RO"] = "INT1", ["INT_KATAKANAHIRAGANA"] = "INT2",
        ["INT_YEN"] = "INT3", ["INT_HENKAN"] = "INT4", ["INT_MUHENKAN"] = "INT5",
        ["LANG1"] = "INT2", ["LANG2"] = "INT5",
    };

    /// <summary>
    /// ZMK が <c>LS(...)</c> の別名として定義しているキーコード。
    /// US 配列を前提に名付けられているので、JIS では名前と出る文字がずれる
    /// （<c>AT</c> は Shift+2 なので JIS では <c>"</c> になる）。
    /// 実際に出る文字を出したいので、名前ではなく展開先で解決する。
    /// </summary>
    private static readonly Dictionary<string, string> ShiftedAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EXCL"] = "N1", ["EXCLAMATION"] = "N1",
        ["AT"] = "N2", ["AT_SIGN"] = "N2",
        ["HASH"] = "N3", ["POUND"] = "N3",
        ["DLLR"] = "N4", ["DOLLAR"] = "N4",
        ["PRCNT"] = "N5", ["PERCENT"] = "N5",
        ["CARET"] = "N6",
        ["AMPS"] = "N7", ["AMPERSAND"] = "N7",
        ["STAR"] = "N8", ["ASTRK"] = "N8", ["ASTERISK"] = "N8",
        ["LPAR"] = "N9", ["LEFT_PARENTHESIS"] = "N9",
        ["RPAR"] = "N0", ["RIGHT_PARENTHESIS"] = "N0",
        ["UNDER"] = "MINUS", ["UNDERSCORE"] = "MINUS",
        ["PLUS"] = "EQUAL",
        ["LBRC"] = "LBKT", ["LEFT_BRACE"] = "LBKT",
        ["RBRC"] = "RBKT", ["RIGHT_BRACE"] = "RBKT",
        ["PIPE"] = "BSLH",
        ["COLON"] = "SEMI",
        ["DQT"] = "SQT", ["DOUBLE_QUOTES"] = "SQT",
        ["TILDE"] = "GRAVE",
        ["LT"] = "COMMA", ["LESS_THAN"] = "COMMA",
        ["GT"] = "DOT", ["GREATER_THAN"] = "DOT",
        ["QMARK"] = "SLASH", ["QUESTION"] = "SLASH",
    };

    /// <summary>文字を出さないキー。配列によらず同じ表示。</summary>
    private static readonly Dictionary<string, string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENTER"] = "Enter", ["RET"] = "Enter", ["RETURN"] = "Enter",
        ["ESC"] = "Esc", ["ESCAPE"] = "Esc",
        ["BSPC"] = "BS", ["BACKSPACE"] = "BS",
        ["DEL"] = "Del", ["DELETE"] = "Del",
        ["TAB"] = "Tab",
        ["SPACE"] = "Space",
        ["CAPS"] = "Caps", ["CAPSLOCK"] = "Caps", ["CAPS_LOCK"] = "Caps",
        ["INS"] = "Ins", ["INSERT"] = "Ins",
        ["HOME"] = "Home", ["END"] = "End",
        ["PG_UP"] = "PgUp", ["PAGE_UP"] = "PgUp",
        ["PG_DN"] = "PgDn", ["PAGE_DOWN"] = "PgDn",

        ["LEFT"] = "←", ["LEFT_ARROW"] = "←",
        ["RIGHT"] = "→", ["RIGHT_ARROW"] = "→",
        ["UP"] = "↑", ["UP_ARROW"] = "↑",
        ["DOWN"] = "↓", ["DOWN_ARROW"] = "↓",

        ["PSCRN"] = "PrtSc", ["PRINTSCREEN"] = "PrtSc",
        ["SLCK"] = "ScrLk", ["SCROLLLOCK"] = "ScrLk",
        ["PAUSE_BREAK"] = "Pause",
        ["K_APP"] = "Menu", ["K_CMENU"] = "Menu", ["K_CONTEXT_MENU"] = "Menu",

        ["LCTRL"] = "Ctrl", ["LEFT_CONTROL"] = "Ctrl",
        ["RCTRL"] = "Ctrl", ["RIGHT_CONTROL"] = "Ctrl",
        ["LSHFT"] = "Shift", ["LEFT_SHIFT"] = "Shift",
        ["RSHFT"] = "Shift", ["RIGHT_SHIFT"] = "Shift",
        ["LALT"] = "Alt", ["LEFT_ALT"] = "Alt",
        ["RALT"] = "Alt", ["RIGHT_ALT"] = "Alt",
        ["LGUI"] = "Win", ["LEFT_GUI"] = "Win", ["LEFT_WIN"] = "Win", ["LEFT_COMMAND"] = "Win",
        ["RGUI"] = "Win", ["RIGHT_GUI"] = "Win", ["RIGHT_WIN"] = "Win", ["RIGHT_COMMAND"] = "Win",

        ["KP_NUM"] = "NumLk", ["KP_NUMLOCK"] = "NumLk",
        ["KP_DIVIDE"] = "/", ["KP_MULTIPLY"] = "*",
        ["KP_MINUS"] = "-", ["KP_SUBTRACT"] = "-",
        ["KP_PLUS"] = "+", ["KP_ADD"] = "+",
        ["KP_ENTER"] = "Enter", ["KP_DOT"] = ".", ["KP_EQUAL"] = "=",

        ["C_MUTE"] = "Mute", ["C_VOL_UP"] = "Vol+", ["C_VOLUME_UP"] = "Vol+",
        ["C_VOL_DN"] = "Vol−", ["C_VOLUME_DOWN"] = "Vol−",
        ["C_PP"] = "▶⏸", ["C_PLAY_PAUSE"] = "▶⏸",
        ["C_NEXT"] = "⏭", ["C_PREV"] = "⏮",
    };

    /// <summary><see cref="NamedKeys"/> のうち、表示言語で名前が変わるもの。</summary>
    private static readonly Dictionary<string, (string Ja, string En)> LocalizedNamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C_BRI_UP"] = ("輝度+", "Bri+"),
        ["C_BRI_DN"] = ("輝度−", "Bri−"),
    };

    /// <summary>修飾関数の接頭辞。<c>LS()</c> だけは配列のシフト面で解決するので別扱い。</summary>
    private static readonly Dictionary<string, string> ModifierFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LC"] = "^", ["RC"] = "^",
        ["LA"] = "Alt", ["RA"] = "Alt",
        ["LG"] = "Win", ["RG"] = "Win",
    };

    /// <summary>
    /// キーコード名を表示用の文字列にする。
    /// <paramref name="overrides"/> は利用者が config で指定した差し替え。
    /// </summary>
    public static string Label(
        string keycode, HostLayout layout, IReadOnlyDictionary<string, string>? overrides = null)
        => Resolve(keycode.Trim(), layout, overrides, shifted: false);

    private static string Resolve(
        string keycode, HostLayout layout, IReadOnlyDictionary<string, string>? overrides, bool shifted)
    {
        if (keycode.Length == 0) return "";

        if (overrides is not null && overrides.TryGetValue(keycode, out var custom)) return custom;

        // 修飾関数。LS(LC(X)) のような入れ子があるので中から順に解く。
        if (TrySplitFunction(keycode, out var function, out var inner))
        {
            if (function.Equals("LS", StringComparison.OrdinalIgnoreCase) ||
                function.Equals("RS", StringComparison.OrdinalIgnoreCase))
                return Resolve(inner, layout, overrides, shifted: true);

            if (ModifierFunctions.TryGetValue(function, out var prefix))
                return prefix + Resolve(inner, layout, overrides, shifted);

            // 知らない関数はそのまま見せる。表に無いことが分かるように。
            return keycode;
        }

        // シフト面の別名（EXCLAMATION など）は、展開先のシフト面で解決する。
        if (ShiftedAliases.TryGetValue(keycode, out var aliasBase))
            return Resolve(aliasBase, layout, overrides, shifted: true);

        var name = Synonyms.TryGetValue(keycode, out var canonical) ? canonical : keycode;

        if (layout == HostLayout.Jis && JisKeyNames.TryGetValue(name, out var jisName))
            return Strings.T(jisName.Ja, jisName.En);

        if (Faces.TryGetValue(name, out var face))
        {
            var text = layout == HostLayout.Jis
                ? (shifted ? face.JisShift : face.JisBase)
                : (shifted ? face.UsShift : face.UsBase);

            // その配列に面が無いキー（US の INT1 など）は名前で見せる。
            if (text.Length == 0) return shifted ? "⇧" + name : name;

            return text;
        }

        if (NamedKeys.TryGetValue(name, out var named))
            return shifted ? "⇧" + named : named;

        if (LocalizedNamedKeys.TryGetValue(name, out var localized))
        {
            var text = Strings.T(localized.Ja, localized.En);
            return shifted ? "⇧" + text : text;
        }

        // 英字と数字。ZMK では A や N1 以外に素の 1 桁も来ない想定だが念のため。
        if (name.Length == 1 && char.IsLetterOrDigit(name[0]))
            return name.ToUpperInvariant();

        if (IsFunctionKey(name)) return shifted ? "⇧" + name.ToUpperInvariant() : name.ToUpperInvariant();

        // KP_NUMBER_7 のようなテンキー数字。
        if (name.StartsWith("KP_", StringComparison.OrdinalIgnoreCase))
        {
            var rest = name[3..];
            if (rest.StartsWith("NUMBER_", StringComparison.OrdinalIgnoreCase)) rest = rest[7..];
            if (rest.StartsWith("N", StringComparison.OrdinalIgnoreCase) && rest.Length == 2) rest = rest[1..];
            if (rest.Length == 1 && char.IsDigit(rest[0])) return rest;
        }

        return shifted ? "⇧" + name : name;
    }

    private static bool IsFunctionKey(string name) =>
        name.Length is >= 2 and <= 3
        && (name[0] == 'F' || name[0] == 'f')
        && int.TryParse(name[1..], out var n)
        && n is >= 1 and <= 24;

    /// <summary><c>LS(SEMI)</c> を <c>LS</c> と <c>SEMI</c> に割る。</summary>
    private static bool TrySplitFunction(string text, out string function, out string inner)
    {
        function = "";
        inner = "";

        var open = text.IndexOf('(');
        if (open <= 0 || !text.EndsWith(")", StringComparison.Ordinal)) return false;

        function = text[..open].Trim();
        inner = text[(open + 1)..^1].Trim();

        return function.Length > 0
               && function.All(c => char.IsLetter(c) || c == '_');
    }

    /// <summary>修飾キーの名前かどうか。hold 側の表示を短くするのに使う。</summary>
    public static bool IsModifier(string keycode) =>
        NamedKeys.TryGetValue(keycode.Trim(), out var label)
        && label is "Ctrl" or "Shift" or "Alt" or "Win";
}
