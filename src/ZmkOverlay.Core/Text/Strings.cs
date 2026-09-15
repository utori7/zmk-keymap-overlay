using System.Globalization;

namespace ZmkOverlay.Core.Text;

public enum UiLanguage
{
    Ja,
    En,
}

/// <summary>
/// 画面に出る文言。日本語と英語を同じ場所に並べて持つ。
///
/// .resx にしていないのは、単一 exe に衛星アセンブリを抱えずに済み、
/// 2 言語を見比べながら直せるため。<see cref="T"/> は引数を 2 つ取るので、
/// 片方を書き忘れるとコンパイルが通らない。
///
/// UI 側だけで使う文言は ZmkOverlay.App の UiText にある。
/// </summary>
public static class Strings
{
    public static UiLanguage Language { get; set; } = FromSetting(null);

    /// <summary>設定値 "auto" | "ja" | "en" を解決する。auto は Windows の表示言語に従う。</summary>
    public static UiLanguage FromSetting(string? setting) => setting?.Trim().ToLowerInvariant() switch
    {
        "ja" => UiLanguage.Ja,
        "en" => UiLanguage.En,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? UiLanguage.Ja : UiLanguage.En,
    };

    public static string T(string ja, string en) => Language == UiLanguage.Ja ? ja : en;

    // ---- プリプロセッサ ----

    public static string IncludeCycle(string path) =>
        T($"include が循環しています: {path}", $"Circular #include: {path}");

    public static string DirectiveIgnored(string directive) =>
        T($"#{directive} は解釈しません（本文はそのまま残します）",
          $"#{directive} is not evaluated (its body is kept as is)");

    public static string IncludeNotFound(string name) =>
        T($"include を解決できません: {name}", $"Cannot resolve #include: {name}");

    public static string MacroTooDeep =>
        T("マクロ展開が深すぎます。循環している可能性があります。",
          "Macro expansion is too deep; a macro may refer to itself.");

    // ---- devicetree パーサ ----

    public static string AtLine(int line, string message) =>
        T($"{line} 行目: {message}", $"Line {line}: {message}");

    public static string UnexpectedEndAfter(string name) =>
        T($"'{name}' の後で入力が尽きました。", $"Unexpected end of input after '{name}'.");

    public static string UnexpectedCharAfter(string name, char c) =>
        T($"'{name}' の後に予期しない文字 '{c}' があります。", $"Unexpected character '{c}' after '{name}'.");

    public static string PropertyNotClosed(string name) =>
        T($"プロパティ '{name}' が閉じていません。", $"Property '{name}' is not terminated.");

    public static string UnexpectedCharInProperty(string name, char c) =>
        T($"プロパティ '{name}' に予期しない文字 '{c}' があります。",
          $"Unexpected character '{c}' in property '{name}'.");

    public static string NotClosed(char open) =>
        T($"'{open}' が閉じていません。", $"'{open}' is not closed.");

    public static string Expected(char expected, char? actual) => actual is { } found
        ? T($"'{expected}' が必要ですが '{found}' でした。", $"Expected '{expected}' but found '{found}'.")
        : T($"'{expected}' が必要ですが 入力の終わり でした。", $"Expected '{expected}' but reached the end of input.");

    // ---- 読み込み ----

    public static string LayoutUnreadable(string path) =>
        T($"レイアウトを読めません: {path}", $"Cannot read the layout: {path}");

    public static string KeymapUnreadable(string path) =>
        T($"キーマップを読めません: {path}", $"Cannot read the keymap: {path}");

    public static string LayoutHasNoKeys =>
        T("レイアウトにキーが 1 個もありません。", "The layout has no keys.");

    public static string KeymapHasNoLayers =>
        T("キーマップにレイヤーが 1 個もありません。", "The keymap has no layers.");

    public static string LayerKeyCountMismatch(string layer, int count, string layout, int layoutCount) =>
        T($"レイヤー '{layer}' のキー数が {count} ですが、レイアウト '{layout}' は {layoutCount} キーです。",
          $"Layer '{layer}' has {count} keys, but layout '{layout}' has {layoutCount}.");

    public static string KeymapNotFound(string path) =>
        T($"キーマップが見つかりません: {path}", $"Keymap not found: {path}");

    public static string PhysicalLayoutNotFound(string path) =>
        T($"物理レイアウトが見つかりません: {path}", $"Physical layout not found: {path}");

    public static string PhysicalLayoutMissing =>
        T("キーの並び（物理レイアウト）が見つからず、キーマップの書き方からも推定できませんでした。" +
          "シールドの .dtsi を選んでください。",
          "Could not find where the keys are (physical layout), and could not guess it from the keymap either. " +
          "Choose your shield's .dtsi file.");

    public static string LayoutGuessed =>
        T("キーの並び（物理レイアウト）が見つからないので、キーマップの書き方から推定しました。" +
          "配置が違う場合は、設定画面でシールドの .dtsi を選んでください。",
          "The physical layout was not found, so key positions were guessed from how the keymap is written. " +
          "If they look wrong, choose your shield's .dtsi in Settings.");

    public static string KeymapNodeMissing =>
        T("キーマップ（compatible = \"zmk,keymap\"）が見つかりません。",
          "No keymap (compatible = \"zmk,keymap\") was found.");

    public static string LayerHasNoBindings(string layer) =>
        T($"レイヤー '{layer}' に bindings がありません。", $"Layer '{layer}' has no bindings.");

    public static string LayerKeyCountDiffers(string layer, int count, int layoutCount) =>
        T($"レイヤー '{layer}' のキー数が {count} で、レイアウトの {layoutCount} と一致しません。",
          $"Layer '{layer}' has {count} keys, which does not match the layout's {layoutCount}.");

    public static string PhysicalLayoutHasNoKeys =>
        T("physical-layout に keys がありません。", "The physical-layout has no keys.");

    public static string KeyAttrsTooFew(int count) =>
        T($"key_physical_attrs の値が {count} 個しかありません。",
          $"key_physical_attrs has only {count} values.");
}
