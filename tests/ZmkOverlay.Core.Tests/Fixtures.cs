using System.Text.RegularExpressions;

namespace ZmkOverlay.Core.Tests;

/// <summary>
/// テスト用のファイルの場所。
///
///   fixtures/zmk-config/  このリポジトリのために書いた 40 キーの分割キーボード（demo40）の zmk-config
///   fixtures/zmk/         ZMK 本体の Corne 一式（MIT、ZMK と同じ並び）
/// </summary>
internal static class Fixtures
{
    public static string Config(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "zmk-config", relative);

    public static string Zmk(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "zmk", relative);

    public static string DemoKeymap => Config("config/demo40.keymap");

    public static string DemoShield => Config("config/boards/shields/demo40/demo40.dtsi");

    public static string DemoKeymapText => File.ReadAllText(DemoKeymap);

    public static string CorneKeymap => Zmk("app/boards/shields/corne/corne.keymap");

    public static string CorneShieldFolder => Zmk("app/boards/shields/corne");

    public static string CorneShield => Zmk("app/boards/shields/corne/corne.dtsi");

    /// <summary>テスト出力からリポジトリ直下へ戻る。</summary>
    public static string RepositoryFile(string relative) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relative));

    /// <summary>
    /// docs/examples/pyuron/README.md の手順どおりに、手で合図キーを入れたキーマップにする。
    /// layer-signal.dtsi を include し、親指の 4 つの &amp;lt を合図付きのものに変える。
    /// </summary>
    public static string WithHandMadeSignals(string keymap)
    {
        // include はレイヤー番号の #define より後に置く必要がある。
        var patched = Regex.Replace(keymap, @"^(#define L_SYS .*)$", "$1\n\n#include \"layer-signal.dtsi\"", RegexOptions.Multiline);

        return Regex.Replace(patched, @"&lt (L_(SYM|NAV|FUNC|SYS)) ", m => $"&lt_{m.Groups[2].Value.ToLowerInvariant()} {m.Groups[1].Value} ");
    }
}
