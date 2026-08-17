using System.Text;
using System.Text.RegularExpressions;

namespace ZmkOverlay.Core.Dts;

public sealed class PreprocessResult
{
    public string Text { get; init; } = "";
    public IReadOnlyDictionary<string, string> ObjectMacros { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// devicetree ソース向けの、C プリプロセッサの部分実装。
///
/// ZMK のキーマップが実際に使う範囲だけを扱う。具体的には
/// コメント除去、行継続、ローカル <c>#include</c> の展開、
/// <c>#define</c>（オブジェクト形・関数形）の再帰展開。
///
/// <c>#include &lt;...&gt;</c> のシステムヘッダは展開しない。ZMK のソースは
/// 手元に無いのが普通で、そこから来るキーコード名は
/// <see cref="Zmk.KeycodeTable"/> が内蔵表として持っているため。
/// </summary>
public sealed class Preprocessor
{
    private const int MaxExpansionDepth = 32;

    private sealed record Macro(string Name, IReadOnlyList<string>? Parameters, string Body);

    private static readonly Regex DirectiveRe = new(
        @"^[ \t]*#[ \t]*(include|define|undef|if|ifdef|ifndef|elif|else|endif|pragma|error|warning)\b[ \t]*(.*)$",
        RegexOptions.Compiled);

    // 先頭が '#' の識別子（#binding-cells など）を誤ってディレクティブと
    // 見なさないよう、上の正規表現は既知のディレクティブ名だけを受ける。

    private static readonly Regex ObjectDefineRe = new(
        @"^([A-Za-z_][A-Za-z0-9_]*)[ \t]*(.*)$", RegexOptions.Compiled);

    private static readonly Regex FunctionDefineRe = new(
        @"^([A-Za-z_][A-Za-z0-9_]*)\(([^)]*)\)[ \t]*(.*)$", RegexOptions.Compiled);

    private readonly Dictionary<string, Macro> _macros = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = new();
    private readonly HashSet<string> _includeStack = new(StringComparer.OrdinalIgnoreCase);

    public PreprocessResult ProcessFile(string path)
    {
        var body = Collect(File.ReadAllText(path), Path.GetFullPath(path));

        return new PreprocessResult
        {
            Text = ExpandText(body, new HashSet<string>(StringComparer.Ordinal), 0),
            ObjectMacros = _macros
                .Where(kv => kv.Value.Parameters is null)
                .ToDictionary(kv => kv.Key, kv => kv.Value.Body, StringComparer.Ordinal),
            Warnings = _warnings,
        };
    }

    /// <summary>ファイルを読まずに文字列を処理する。テスト用。</summary>
    public PreprocessResult ProcessText(string text, string? virtualPath = null)
    {
        var body = Collect(text, virtualPath ?? "<text>");

        return new PreprocessResult
        {
            Text = ExpandText(body, new HashSet<string>(StringComparer.Ordinal), 0),
            ObjectMacros = _macros
                .Where(kv => kv.Value.Parameters is null)
                .ToDictionary(kv => kv.Key, kv => kv.Value.Body, StringComparer.Ordinal),
            Warnings = _warnings,
        };
    }

    /// <summary>
    /// コメントと行継続を潰し、ディレクティブを処理して、残った本文を返す。
    /// マクロ展開はすべて集め終わってから一括で行う（include の前後で
    /// 定義順が入れ替わっても解決できるようにするため）。
    /// </summary>
    private string Collect(string source, string path)
    {
        if (!_includeStack.Add(path))
        {
            _warnings.Add($"include が循環しています: {path}");
            return "";
        }

        try
        {
            var text = StripComments(source);
            text = JoinContinuedLines(text);

            var output = new StringBuilder();
            var directory = Path.GetDirectoryName(path) ?? ".";

            foreach (var line in text.Split('\n'))
            {
                var match = DirectiveRe.Match(line);
                if (!match.Success)
                {
                    output.AppendLine(line);
                    continue;
                }

                var directive = match.Groups[1].Value;
                var argument = match.Groups[2].Value.Trim();

                switch (directive)
                {
                    case "define":
                        Define(argument);
                        break;

                    case "undef":
                        _macros.Remove(argument.Trim());
                        break;

                    case "include":
                        output.Append(Include(argument, directory));
                        break;

                    default:
                        // 条件分岐は解釈しない。ZMK のキーマップでは
                        // 機能の有無を切り替える用途がほとんどで、
                        // 本文を残しておくほうが読める結果になる。
                        _warnings.Add($"#{directive} は解釈しません（本文はそのまま残します）");
                        break;
                }

                // 行数を保つと、あとで位置を報告するときに元ファイルと突き合わせやすい。
                output.AppendLine();
            }

            return output.ToString();
        }
        finally
        {
            _includeStack.Remove(path);
        }
    }

    private string Include(string argument, string directory)
    {
        if (argument.StartsWith('<'))
        {
            // システムヘッダ。キーコード名は内蔵表で解決するので読み込まない。
            return "";
        }

        var name = argument.Trim().Trim('"');
        var full = Path.GetFullPath(Path.Combine(directory, name));

        if (!File.Exists(full))
        {
            _warnings.Add($"include を解決できません: {name}");
            return "";
        }

        return Collect(File.ReadAllText(full), full);
    }

    private void Define(string argument)
    {
        var function = FunctionDefineRe.Match(argument);
        if (function.Success && argument.IndexOf('(') < IndexOfFirstSpace(argument))
        {
            var parameters = function.Groups[2].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            _macros[function.Groups[1].Value] =
                new Macro(function.Groups[1].Value, parameters, function.Groups[3].Value.Trim());
            return;
        }

        var obj = ObjectDefineRe.Match(argument);
        if (!obj.Success) return;

        _macros[obj.Groups[1].Value] =
            new Macro(obj.Groups[1].Value, null, obj.Groups[2].Value.Trim());
    }

    /// <summary>
    /// <c>#define FOO(x) ...</c> と <c>#define FOO (x)</c> を見分ける。
    /// 前者は名前の直後が括弧で、後者は空白が挟まる。
    /// </summary>
    private static int IndexOfFirstSpace(string s)
    {
        var i = s.IndexOfAny(new[] { ' ', '\t' });
        return i < 0 ? int.MaxValue : i;
    }

    // ---- 字句レベルの前処理 ----

    private static string StripComments(string source)
    {
        var sb = new StringBuilder(source.Length);
        var i = 0;

        while (i < source.Length)
        {
            var c = source[i];

            if (c == '"')
            {
                var end = i + 1;
                while (end < source.Length && source[end] != '"')
                    end += source[end] == '\\' ? 2 : 1;

                end = Math.Min(end + 1, source.Length);
                sb.Append(source, i, end - i);
                i = end;
                continue;
            }

            if (c == '/' && i + 1 < source.Length)
            {
                if (source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    continue;
                }

                if (source[i + 1] == '*')
                {
                    var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    var stop = end < 0 ? source.Length : end + 2;

                    // 改行は残す。行番号がずれるとエラー報告が使い物にならなくなる。
                    for (var k = i; k < stop; k++)
                        if (source[k] == '\n') sb.Append('\n');

                    i = stop;
                    continue;
                }
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static string JoinContinuedLines(string text) =>
        text.Replace("\\\r\n", "").Replace("\\\n", "");

    // ---- マクロ展開 ----

    private string ExpandText(string text, HashSet<string> active, int depth)
    {
        if (depth > MaxExpansionDepth)
        {
            _warnings.Add("マクロ展開が深すぎます。循環している可能性があります。");
            return text;
        }

        var sb = new StringBuilder(text.Length);
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '"')
            {
                var end = i + 1;
                while (end < text.Length && text[end] != '"')
                    end += text[end] == '\\' ? 2 : 1;

                end = Math.Min(end + 1, text.Length);
                sb.Append(text, i, end - i);
                i = end;
                continue;
            }

            if (!IsIdentStart(c))
            {
                sb.Append(c);
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && IsIdentPart(text[i])) i++;
            var name = text[start..i];

            if (active.Contains(name) || !_macros.TryGetValue(name, out var macro))
            {
                sb.Append(name);
                continue;
            }

            if (macro.Parameters is null)
            {
                active.Add(name);
                sb.Append(ExpandText(macro.Body, active, depth + 1));
                active.Remove(name);
                continue;
            }

            var afterName = i;
            while (afterName < text.Length && char.IsWhiteSpace(text[afterName])) afterName++;

            if (afterName >= text.Length || text[afterName] != '(')
            {
                sb.Append(name);
                continue;
            }

            var arguments = ReadArguments(text, afterName, out var afterCall);
            if (arguments is null || arguments.Count != macro.Parameters.Count)
            {
                sb.Append(name);
                continue;
            }

            active.Add(name);
            sb.Append(ExpandText(Substitute(macro, arguments), active, depth + 1));
            active.Remove(name);
            i = afterCall;
        }

        return sb.ToString();
    }

    /// <summary>括弧の対応を数えながら引数を切り出す。ネストした呼び出しに対応する。</summary>
    private static List<string>? ReadArguments(string text, int openParen, out int afterCall)
    {
        afterCall = openParen;

        var arguments = new List<string>();
        var current = new StringBuilder();
        var depth = 0;

        for (var i = openParen; i < text.Length; i++)
        {
            var c = text[i];

            switch (c)
            {
                case '(':
                    depth++;
                    if (depth == 1) continue;
                    break;

                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        arguments.Add(current.ToString().Trim());
                        afterCall = i + 1;
                        return arguments;
                    }
                    break;

                case ',' when depth == 1:
                    arguments.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
            }

            current.Append(c);
        }

        return null;
    }

    private static string Substitute(Macro macro, IReadOnlyList<string> arguments)
    {
        var sb = new StringBuilder();
        var body = macro.Body;
        var i = 0;

        while (i < body.Length)
        {
            if (!IsIdentStart(body[i]))
            {
                sb.Append(body[i]);
                i++;
                continue;
            }

            var start = i;
            while (i < body.Length && IsIdentPart(body[i])) i++;
            var name = body[start..i];

            var index = -1;
            for (var p = 0; p < macro.Parameters!.Count; p++)
                if (macro.Parameters[p] == name) { index = p; break; }

            sb.Append(index >= 0 ? arguments[index] : name);
        }

        return sb.ToString();
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';
}
