using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ZmkOverlay.Core.Dts;
using ZmkOverlay.Core.Text;

namespace ZmkOverlay.Core.Zmk;

/// <summary>合図キーを付けなかったレイヤーの理由。</summary>
public enum SkipReason
{
    /// <summary>&amp;tog / &amp;to / &amp;sl で入る。「押しているあいだ」という合図の仕組みに合わない。</summary>
    ToggleOrOneShot,

    /// <summary>キーマップで作った hold-tap で入る。押し心地の設定を正しく引き継げないので触らない。</summary>
    CustomHoldTap,

    /// <summary>レイヤーに入るキーが無い（トラックボールなどで自動で入る、条件付きレイヤーなど）。</summary>
    NoEntryKey,

    /// <summary>F13〜F24 が使い切られている。</summary>
    NoFreeSignalKey,
}

/// <summary>合図キーを付けたレイヤー。<paramref name="KeyCount"/> は書き換えたキーの数。</summary>
public sealed record PatchedLayer(int Layer, string SignalKey, int KeyCount);

/// <summary>合図キーを付けなかったレイヤー。<paramref name="Detail"/> は自作 hold-tap の名前など。</summary>
public sealed record SkippedLayer(int Layer, SkipReason Reason, string? Detail = null);

/// <summary>書き換えで変わる行。<paramref name="Line"/> は元のファイルでの行番号（1 始まり）。</summary>
public sealed record ChangedLine(int Line, string Before, string After);

public sealed class KeymapPatch
{
    /// <summary>書き換え後のキーマップ全文。</summary>
    public string Text { get; init; } = "";

    /// <summary>元の文字列から変わったか。</summary>
    public bool Changed { get; init; }

    public IReadOnlyList<PatchedLayer> Added { get; init; } = Array.Empty<PatchedLayer>();

    /// <summary>もともと合図キーが仕込まれていたレイヤー（手で入れたものなど）。触らない。</summary>
    public IReadOnlyDictionary<int, string> AlreadySignaled { get; init; } = new Dictionary<int, string>();

    public IReadOnlyList<SkippedLayer> Skipped { get; init; } = Array.Empty<SkippedLayer>();
}

/// <summary>
/// キーマップを、レイヤーに入るときに合図キーを押す形に書き換える。
///
/// 利用者は出力をそのまま zmk-config の .keymap と差し替えればよい。追加のファイルは要らない
/// （GitHub のブラウザ画面で 1 ファイルを貼り替えるだけで済むように）。
///
/// 書き換えは 2 か所だけ。
///   1. レイヤーの中の &amp;mo / &amp;lt のビヘイビア名を、合図付きのものに変える。引数は変えない。
///        &amp;lt L_SYM INT5  →  &amp;zo_lt_l1 L_SYM INT5
///   2. 最初の <c>/ {</c> の直前に、その定義を目印付きで差し込む。
///      中身は実機で成立を確かめた zmk/layer-signal.dtsi と同じ構造。
///
/// 前回の生成分は取り除いてから考えるので、何度かけても同じ結果になる。
/// 目印の区間を消して名前を戻せば、元のキーマップに戻る（<see cref="RemoveGenerated"/>）。
/// </summary>
public static class KeymapPatcher
{
    private const string BeginPrefix = "/* ZMK Keymap Overlay: begin";
    public const string BeginMarker = BeginPrefix + " (generated) */";
    public const string EndMarker = "/* ZMK Keymap Overlay: end */";

    /// <summary>
    /// ZMK 組み込みの &amp;lt と同じ値（app/dts/behaviors/layer_tap.dtsi）。
    /// 押し心地を変えないため。キーマップで &amp;lt を上書きしていれば、その値が後から効く。
    /// </summary>
    private const string BuiltinLtFlavor = "tap-preferred";
    private const int BuiltinLtTappingTermMs = 200;

    private static readonly Regex GeneratedName = new(@"&zo_(mo|lt)_l\d+\b", RegexOptions.Compiled);

    /// <summary>ビヘイビアと、その第 1 引数。</summary>
    private static readonly Regex Call = new(
        @"&([A-Za-z_][A-Za-z0-9_]*)[ \t\r\n]+([A-Za-z0-9_]+|\([^)]*\))", RegexOptions.Compiled);

    private static readonly Regex SignalKeyInUse = new(@"\bF(1[3-9]|2[0-4])\b", RegexOptions.Compiled);
    private static readonly Regex BindingsStart = new(@"\bbindings\s*=\s*<", RegexOptions.Compiled);
    private static readonly Regex RootNode = new(@"^[ \t]*/[ \t]*\{", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex LtOverride = new(@"&lt\s*\{", RegexOptions.Compiled);

    public static KeymapPatch Patch(string source, string keymapPath)
    {
        var text = RemoveGenerated(source);
        var masked = SourceText.MaskComments(text);
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        // 意味の解釈（#define の展開、ビヘイビアの定義、既存の合図キー）は、読み込みと同じ部品で行う。
        var preprocessed = new Preprocessor().ProcessText(text, Path.GetFullPath(keymapPath));
        var tree = DtsParser.Parse(preprocessed.Text);
        var behaviors = ZmkKeymapReader.ReadBehaviors(tree);
        var keymapNode = ZmkKeymapReader.Find(tree, "zmk,keymap")
                         ?? throw new InvalidDataException(Strings.KeymapNodeMissing);

        var already = ZmkKeymapReader.DetectSignalKeys(
            keymapNode.Children
                .Select(n => n.Property("bindings"))
                .Where(p => p is not null)
                .SelectMany(p => ZmkBinding.Split(p!.AllCells)),
            behaviors);

        // 位置は元のテキストの上で数える（コメントは同じ長さの空白に塗ってある）。
        var entries = new List<(int Position, string Behavior, int Layer)>();
        var toggles = new HashSet<int>();
        var customs = new Dictionary<int, string>();

        foreach (var (start, end) in LayerBindingRanges(masked))
        {
            for (var match = Call.Match(masked, start); match.Success && match.Index < end; match = match.NextMatch())
            {
                var name = match.Groups[1].Value;
                if (ResolveLayer(match.Groups[2].Value, preprocessed.ObjectMacros) is not { } layer) continue;

                switch (name)
                {
                    case "mo" or "lt":
                        if (!already.ContainsKey(layer)) entries.Add((match.Index, name, layer));
                        break;

                    case "tog" or "to" or "sl":
                        toggles.Add(layer);
                        break;

                    default:
                        if (behaviors.TryGetValue(name, out var info)
                            && info.IsHoldTap
                            && info.Bindings.Count > 0
                            && info.Bindings[0].Behavior == "mo")
                            customs.TryAdd(layer, name);
                        break;
                }
            }
        }

        // 合図キーを割り当てる。キーマップがすでに使っている F13〜F24 は避ける。
        var used = new HashSet<string>(
            SignalKeyInUse.Matches(masked).Select(m => m.Value.ToUpperInvariant()), StringComparer.Ordinal);
        used.UnionWith(already.Values);

        var free = new Queue<string>(Enumerable.Range(13, 12).Select(n => $"F{n}").Where(k => !used.Contains(k)));
        var assigned = new SortedDictionary<int, string>();
        var skipped = new SortedDictionary<int, SkippedLayer>();

        foreach (var layer in entries.Select(e => e.Layer).Distinct().OrderBy(l => l))
        {
            if (free.Count > 0) assigned[layer] = free.Dequeue();
            else skipped[layer] = new SkippedLayer(layer, SkipReason.NoFreeSignalKey);
        }

        bool Handled(int layer) => assigned.ContainsKey(layer) || already.ContainsKey(layer) || skipped.ContainsKey(layer);

        foreach (var layer in toggles.Where(l => !Handled(l)))
            skipped[layer] = new SkippedLayer(layer, SkipReason.ToggleOrOneShot);

        foreach (var (layer, name) in customs.Where(c => !Handled(c.Key)))
            skipped[layer] = new SkippedLayer(layer, SkipReason.CustomHoldTap, name);

        for (var layer = 1; layer < keymapNode.Children.Count; layer++)
            if (!Handled(layer)) skipped[layer] = new SkippedLayer(layer, SkipReason.NoEntryKey);

        var result = new StringBuilder(text);

        if (assigned.Count > 0)
        {
            // 後ろから置き換えると、前の位置がずれない。
            foreach (var entry in entries.Where(e => assigned.ContainsKey(e.Layer)).OrderByDescending(e => e.Position))
            {
                result.Remove(entry.Position, entry.Behavior.Length + 1);
                result.Insert(entry.Position, $"&zo_{entry.Behavior}_l{entry.Layer}");
            }

            // 定義は最初のルートノードの前に置く。#include と #define より後になるので、
            // MACRO_PLACEHOLDER や F13 などの名前が使える。置き換えはすべてこれより後ろにある。
            var root = RootNode.Match(masked);
            var insertAt = root.Success ? root.Index : text.Length;

            var usesLt = entries.Where(e => e.Behavior == "lt").Select(e => e.Layer).ToHashSet();
            var block = BuildBlock(assigned, usesLt, LtOverrideBody(text, masked), newline);

            result.Insert(insertAt, block);
        }

        var patched = result.ToString();

        return new KeymapPatch
        {
            Text = patched,
            Changed = patched != source,
            Added = assigned
                .Select(a => new PatchedLayer(a.Key, a.Value, entries.Count(e => e.Layer == a.Key)))
                .ToList(),
            AlreadySignaled = already,
            Skipped = skipped.Values.ToList(),
        };
    }

    /// <summary>
    /// 生成した区間を取り除き、書き換えたビヘイビア名を元に戻す。
    /// <see cref="Patch"/> の出力に対しては、元のキーマップがそのまま返る。
    /// </summary>
    public static string RemoveGenerated(string source) =>
        GeneratedName.Replace(RemoveBlock(source), m => "&" + m.Groups[1].Value);

    /// <summary>生成した定義の区間（目印を含む）。無ければ null。利用者に変更点を見せるのに使う。</summary>
    public static string? GeneratedBlock(string text)
    {
        var begin = text.IndexOf(BeginPrefix, StringComparison.Ordinal);
        if (begin < 0) return null;

        var end = text.IndexOf(EndMarker, begin, StringComparison.Ordinal);
        return end < 0 ? null : text[begin..(end + EndMarker.Length)];
    }

    /// <summary>
    /// 書き換えで変わる行。差し込んだ定義の区間は含めない（<see cref="GeneratedBlock"/> で別に見せる）。
    /// 「どこが変わるのか」を、貼り替える前に利用者が確かめられるようにするため。
    /// </summary>
    public static IReadOnlyList<ChangedLine> ChangedLines(string original, string patched)
    {
        var before = SplitLines(original);
        var after = SplitLines(RemoveBlock(patched));

        // 区間を除けば行数は同じになるはず。違うなら元が別のファイル。
        if (before.Length != after.Length) return Array.Empty<ChangedLine>();

        var changed = new List<ChangedLine>();
        for (var i = 0; i < before.Length; i++)
            if (before[i] != after[i]) changed.Add(new ChangedLine(i + 1, before[i], after[i]));

        return changed;
    }

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Split('\n');

    /// <summary>目印の区間だけを取り除く。ビヘイビア名は戻さない。</summary>
    private static string RemoveBlock(string text)
    {
        var begin = text.IndexOf(BeginPrefix, StringComparison.Ordinal);
        if (begin < 0) return text;

        var end = text.IndexOf(EndMarker, begin, StringComparison.Ordinal);
        if (end < 0) return text;

        end += EndMarker.Length;

        // 差し込むときに付けた改行 2 つ（区間の後の空行）も一緒に消す。
        for (var i = 0; i < 2; i++)
        {
            if (string.CompareOrdinal(text, end, "\r\n", 0, 2) == 0) end += 2;
            else if (end < text.Length && text[end] == '\n') end += 1;
        }

        return text.Remove(begin, end - begin);
    }

    /// <summary>キーマップノードの中にある、各レイヤーの <c>bindings = &lt; … &gt;</c> の中身の範囲。</summary>
    private static List<(int Start, int End)> LayerBindingRanges(string masked)
    {
        var ranges = new List<(int, int)>();

        var at = masked.IndexOf("\"zmk,keymap\"", StringComparison.Ordinal);
        if (at < 0) return ranges;

        var open = masked.LastIndexOf('{', at);
        if (open < 0) return ranges;

        var close = MatchingBrace(masked, open);

        for (var match = BindingsStart.Match(masked, at); match.Success && match.Index < close;)
        {
            var start = match.Index + match.Length;
            var end = masked.IndexOf('>', start);
            if (end < 0 || end > close) break;

            ranges.Add((start, end));
            match = BindingsStart.Match(masked, end);
        }

        return ranges;
    }

    private static int MatchingBrace(string text, int open)
    {
        var depth = 0;

        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return i;
        }

        return text.Length;
    }

    /// <summary>レイヤー番号。<c>L_SYM</c> のような名前は <c>#define</c> をたどって数値にする。</summary>
    private static int? ResolveLayer(string token, IReadOnlyDictionary<string, string> macros, int depth = 0)
    {
        var value = token.Trim().TrimStart('(').TrimEnd(')').Trim();

        if (DtsValue.TryParseNumber(value, out var number)) return number;

        return depth < 8 && macros.TryGetValue(value, out var body)
            ? ResolveLayer(body, macros, depth + 1)
            : null;
    }

    /// <summary>キーマップが <c>&amp;lt { … };</c> で組み込みの設定を変えていれば、その中身（コメントも残す）。</summary>
    private static string? LtOverrideBody(string text, string masked)
    {
        var match = LtOverride.Match(masked);
        if (!match.Success) return null;

        var open = match.Index + match.Length - 1;
        var close = MatchingBrace(masked, open);
        if (close >= text.Length) return null;

        return text[(open + 1)..close];
    }

    private static string BuildBlock(
        SortedDictionary<int, string> assigned, HashSet<int> usesLt, string? ltOverride, string newline)
    {
        var lines = new List<string>
        {
            BeginMarker,
            "/*",
            " * Signal keys for the ZMK Keymap Overlay PC app.",
            " * Entering a layer also holds an F13-F24 key, so the PC can show that layer.",
            " * Regenerate this block from the app instead of editing it by hand.",
            " * The &zo_lt_* keys keep the timing of ZMK's built-in &lt",
            " * (and of any &lt { ... } override in this keymap).",
            " */",
            "/ {",
            "    macros {",
        };

        foreach (var (layer, key) in assigned)
        {
            // wait-ms / tap-ms を 0 にしないと、バインディングのあいだに既定の待ちが入り、
            // レイヤーに入るのが遅れて直後の打鍵が下のレイヤーに落ちる。
            // &mo を合図キーより先に押すのも同じ理由。
            lines.AddRange(new[]
            {
                $"        zo_mo_l{layer}: zo_mo_l{layer} {{",
                "            compatible = \"zmk,behavior-macro-one-param\";",
                "            #binding-cells = <1>;",
                "            wait-ms = <0>;",
                "            tap-ms = <0>;",
                "            bindings",
                "                = <&macro_press &macro_param_1to1 &mo MACRO_PLACEHOLDER>",
                $"                , <&macro_press &kp {key}>",
                "                , <&macro_pause_for_release>",
                $"                , <&macro_release &kp {key}>",
                "                , <&macro_release &macro_param_1to1 &mo MACRO_PLACEHOLDER>;",
                "        };",
            });
        }

        lines.Add("    };");

        var holdTaps = assigned.Keys.Where(usesLt.Contains).ToList();
        if (holdTaps.Count > 0)
        {
            lines.Add("");
            lines.Add("    behaviors {");

            foreach (var layer in holdTaps)
            {
                lines.AddRange(new[]
                {
                    $"        zo_lt_l{layer}: zo_lt_l{layer} {{",
                    "            compatible = \"zmk,behavior-hold-tap\";",
                    "            #binding-cells = <2>;",
                    $"            flavor = \"{BuiltinLtFlavor}\";",
                    $"            tapping-term-ms = <{BuiltinLtTappingTermMs.ToString(CultureInfo.InvariantCulture)}>;",
                });

                // 後に書いたプロパティが勝つので、キーマップでの上書きは既定値の後に写す。
                if (ltOverride is not null)
                {
                    foreach (var line in ltOverride.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
                        lines.Add("            " + line);
                }

                lines.Add($"            bindings = <&zo_mo_l{layer}>, <&kp>;");
                lines.Add("        };");
            }

            lines.Add("    };");
        }

        lines.Add("};");
        lines.Add(EndMarker);

        // 区間の後に空行を 1 つ置く。RemoveGenerated はこの改行 2 つまで一緒に消す。
        return string.Join(newline, lines) + newline + newline;
    }
}
