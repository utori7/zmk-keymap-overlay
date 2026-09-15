namespace ZmkOverlay.Core.Dts;

/// <summary>
/// devicetree ソースを、文字の位置を変えずに扱うための下ごしらえ。
///
/// <see cref="Preprocessor"/> のコメント除去は文字を詰めるので、元のファイルの桁や位置が分からなくなる。
/// キーマップの書き方から並びを推定したり、元のファイルの一部だけを書き換えたりするには、
/// 位置がそのまま残っている必要がある。
/// </summary>
public static class SourceText
{
    /// <summary>
    /// コメントを同じ長さの空白に置き換える。改行は残し、文字列リテラルの中は触らない。
    /// </summary>
    public static string MaskComments(string source)
    {
        var chars = source.ToCharArray();
        var i = 0;

        while (i < chars.Length)
        {
            var c = chars[i];

            if (c == '"')
            {
                var end = i + 1;
                while (end < chars.Length && chars[end] != '"')
                    end += chars[end] == '\\' ? 2 : 1;

                i = Math.Min(end + 1, chars.Length);
                continue;
            }

            if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
            {
                while (i < chars.Length && chars[i] != '\n')
                {
                    if (chars[i] != '\r') chars[i] = ' ';
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
            {
                var close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = close < 0 ? chars.Length : close + 2;

                for (var k = i; k < stop; k++)
                    if (chars[k] is not ('\n' or '\r')) chars[k] = ' ';

                i = stop;
                continue;
            }

            i++;
        }

        return new string(chars);
    }
}
