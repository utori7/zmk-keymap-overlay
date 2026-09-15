using System.Text.RegularExpressions;
using ZmkOverlay.Core.Dts;
using ZmkOverlay.Core.Model;

namespace ZmkOverlay.Core.Zmk;

/// <summary>
/// 物理レイアウトがどこにも見つからないときに、キーマップの書き方からキーの並びを推定する。
///
/// ZMK のキーマップは、たいてい 1 行 = キーボードの横一列で書かれ、分割キーボードなら
/// 左右の手のあいだを大きな空白で空けてある。その行と空白を読んで、おおよその配置を作る。
///
/// 正確さは保証しない。親指の行のように、キーマップに隙間が書かれていない位置は再現できない。
/// 画面のプレビューで利用者に確かめてもらう前提の、最後の手段。
/// </summary>
public static class LayoutGuesser
{
    /// <summary>1 行にこれより多く並んでいたら、行がキーボードの列を表していないとみなす。</summary>
    private const int MaxKeysPerRow = 16;

    private static readonly Regex BindingsStart = new(@"\bbindings\s*=\s*<", RegexOptions.Compiled);

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

        var match = BindingsStart.Match(masked, keymap);
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

    /// <summary>行ごとの、左手側のキー数。左右に分かれていない行は null。</summary>
    private static int?[] FindSplits(List<List<Token>> rows)
    {
        var splits = new int?[rows.Count];

        var gaps = rows
            .SelectMany(r => r.Zip(r.Skip(1), (a, b) => b.Start - a.End))
            .OrderBy(g => g)
            .ToList();

        if (gaps.Count == 0) return splits;

        // 左右の手のあいだの空白は、キー同士のふつうの空白よりずっと広い。
        var typical = gaps[gaps.Count / 2];
        var threshold = Math.Max(6, typical * 3);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var widestAt = -1;
            var widest = 0;

            for (var k = 0; k + 1 < row.Count; k++)
            {
                var gap = row[k + 1].Start - row[k].End;
                if (gap <= widest) continue;

                widest = gap;
                widestAt = k;
            }

            if (widestAt >= 0 && widest >= threshold) splits[i] = widestAt + 1;
        }

        // 分割キーボードでも、左右を詰めて書いた行がある（Pyuron の下段など）。
        // 他の行が分かれていて、この行が偶数個なら、真ん中で分かれているとみなす。
        if (splits.Any(s => s is not null))
        {
            for (var i = 0; i < rows.Count; i++)
                if (splits[i] is null && rows[i].Count % 2 == 0) splits[i] = rows[i].Count / 2;
        }

        return splits;
    }

    /// <summary>左手は左端から、右手は右端に揃えて並べる。左右のあいだは 1u 空ける。</summary>
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

        var rightEdge = maxLeft + 1 + maxRight;
        var layout = new PhysicalLayout { Name = "Guessed" };

        for (var i = 0; i < rows.Count; i++)
        {
            var count = rows[i].Count;
            var left = splits[i] ?? count;

            for (var k = 0; k < count; k++)
            {
                var column = k < left ? k : rightEdge - (count - left) + (k - left);
                layout.Keys.Add(new PhysicalKey { X = column * 100, Y = i * 100 });
            }
        }

        return layout;
    }
}
