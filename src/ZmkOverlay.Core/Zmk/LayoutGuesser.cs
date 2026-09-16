using ZmkOverlay.Core.Dts;
using ZmkOverlay.Core.Model;

namespace ZmkOverlay.Core.Zmk;

/// <summary>
/// 物理レイアウトがどこにも見つからないときに、キーマップの書き方からキーの並びを推定する。
///
/// ZMK のキーマップは、たいてい 1 行 = キーボードの横一列で書かれ、分割キーボードなら
/// 左右の手のあいだを大きな空白で空けてある。その行と空白を読んで、おおよその配置を作る。
///
/// 正確さは保証しない。画面のプレビューで利用者に確かめてもらう前提の、最後の手段。
/// </summary>
public static class LayoutGuesser
{
    /// <summary>1 行にこれより多く並んでいたら、行がキーボードの列を表していないとみなす。</summary>
    private const int MaxKeysPerRow = 16;

    /// <summary>左右の分かれ目とみなす空白の最小幅（文字数）。ふつうの空白の 3 倍が目安で、1 文字区切りなら 3。</summary>
    private const int MinSplitGap = 3;

    /// <summary>基準の行よりこれだけ右から書き始めている短い行は、字下げして位置を示しているとみなす。</summary>
    private const int MinIndent = 4;

    /// <summary>キーマップの元のテキストから推定する。行の見分けがつかなければ null。</summary>
    /// <param name="expectedKeys">最初のレイヤーのキー数。推定した数と合わなければ採用しない。</param>
    public static PhysicalLayout? Guess(string keymapSource, int expectedKeys)
    {
        var rows = ReadFirstLayerRows(SourceText.MaskComments(keymapSource));

        if (rows is null || rows.Count == 0) return null;
        if (rows.Sum(r => r.Count) != expectedKeys) return null;
        if (rows.Any(r => r.Count > MaxKeysPerRow)) return null;

        return Place(rows, FindSplits(rows));
    }

    /// <summary>キー 1 個ぶんの書き始めと書き終わりの桁。</summary>
    private sealed record Token(int Start, int End);

    /// <summary>最初のレイヤーの bindings を、行ごとのキーに分ける。</summary>
    private static List<List<Token>>? ReadFirstLayerRows(string masked)
    {
        var keymap = masked.IndexOf("\"zmk,keymap\"", StringComparison.Ordinal);
        if (keymap < 0) return null;

        var match = KeymapPatcher.BindingsStart.Match(masked, keymap);
        if (!match.Success) return null;

        var start = match.Index + match.Length;
        var end = masked.IndexOf('>', start);
        if (end < 0) return null;

        var rows = new List<List<Token>>();

        foreach (var rawLine in masked[start..end].Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var tokens = new List<Token>();

            // バインディングは & で始まる。次の & までがそのキーの書き方。
            var at = line.IndexOf('&');
            while (at >= 0)
            {
                var next = line.IndexOf('&', at + 1);
                var text = next < 0 ? line[at..] : line[at..next];

                tokens.Add(new Token(at, at + text.TrimEnd().Length));
                at = next;
            }

            if (tokens.Count > 0) rows.Add(tokens);
        }

        return rows;
    }

    private static int Gap(List<Token> row, int k) => row[k + 1].Start - row[k].End;

    /// <summary>行ごとの、左手側のキー数。左右に分かれていない行は null。</summary>
    private static int?[] FindSplits(List<List<Token>> rows)
    {
        var splits = new int?[rows.Count];

        var gaps = rows
            .SelectMany(r => Enumerable.Range(0, Math.Max(0, r.Count - 1)).Select(k => Gap(r, k)))
            .OrderBy(g => g)
            .ToList();

        if (gaps.Count == 0) return splits;

        // 左右の手のあいだの空白は、キー同士のふつうの空白よりずっと広い。
        var typical = gaps[gaps.Count / 2];
        var threshold = Math.Max(MinSplitGap, typical * 3);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            // 広い空白が何か所かあるときは（行頭のキーの後で桁を揃えている、など）、行の中央に近いものを選ぶ。
            var candidates = Enumerable.Range(0, Math.Max(0, row.Count - 1))
                .Where(k => Gap(row, k) >= threshold)
                .OrderBy(k => Math.Abs(k + 1 - row.Count / 2.0))
                .ThenByDescending(k => Gap(row, k))
                .ToList();

            if (candidates.Count > 0) splits[i] = candidates[0] + 1;
        }

        // 分かれ目が見つかった行が半分に満たなければ、たまたま広い空白があっただけとみなす。
        var multiKeyRows = rows.Count(r => r.Count > 1);
        if (splits.Count(s => s is not null) * 2 < multiKeyRows)
            return new int?[rows.Count];

        // 分割キーボードでも、左右を詰めて書いた行がある。
        // 他の行が分かれていて、この行が偶数個なら、真ん中で分かれているとみなす。
        if (splits.Any(s => s is not null))
        {
            for (var i = 0; i < rows.Count; i++)
                if (splits[i] is null && rows[i].Count % 2 == 0) splits[i] = rows[i].Count / 2;
        }

        return splits;
    }

    /// <summary>
    /// 左手は左端から、右手は右端に揃えて並べる。左右のあいだは 1u 空ける。
    ///
    /// ただし、いちばん長い行より字下げして書かれた短い行（Corne の親指の段など）は、
    /// 左右とも手のあいだ寄りに置く。分かれていない短い行は中央に置く。
    /// </summary>
    private static PhysicalLayout Place(List<List<Token>> rows, int?[] splits)
    {
        var maxLeft = 0;
        var maxRight = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            if (splits[i] is not { } left) continue;

            maxLeft = Math.Max(maxLeft, left);
            maxRight = Math.Max(maxRight, rows[i].Count - left);
        }

        var isSplit = splits.Any(s => s is not null);
        var rightStart = maxLeft + 1;
        var rightEdge = rightStart + maxRight;

        var widest = rows.Max(r => r.Count);
        var reference = rows.First(r => r.Count == widest);
        var width = isSplit ? rightEdge : widest;

        bool Indented(List<Token> row) => row.Count < widest && row[0].Start - reference[0].Start >= MinIndent;

        var layout = new PhysicalLayout { Name = "Guessed" };

        for (var i = 0; i < rows.Count; i++)
        {
            var count = rows[i].Count;
            var indented = Indented(rows[i]);

            for (var k = 0; k < count; k++)
            {
                double column;

                if (splits[i] is { } left)
                {
                    var right = count - left;

                    column = k < left
                        ? (indented ? maxLeft - left : 0) + k
                        : (indented ? rightStart : rightEdge - right) + (k - left);
                }
                else
                {
                    column = (indented ? (width - count) / 2.0 : 0) + k;
                }

                layout.Keys.Add(new PhysicalKey { X = (int)Math.Round(column * 100), Y = i * 100 });
            }
        }

        return layout;
    }
}
