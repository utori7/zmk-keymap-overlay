using System.Text;

namespace ZmkOverlay.Core.Dts;

public sealed class DtsParseException : Exception
{
    public DtsParseException(string message, int line) : base($"{line} 行目: {message}")
        => Line = line;

    public int Line { get; }
}

/// <summary>
/// devicetree ソースの部分パーサ。
///
/// プリプロセッサ済みのテキストを受け取り、ノード木にする。
/// 文法は ZMK のキーマップとシールド定義が使う範囲に絞ってある。
/// 値の意味づけ（キーコードやビヘイビアの解釈）はここではやらない。
/// </summary>
public sealed class DtsParser
{
    private readonly string _text;
    private int _pos;
    private int _line = 1;

    private DtsParser(string text) => _text = text;

    /// <summary>
    /// トップレベルの <c>/ { }</c> や <c>&amp;label { }</c> をすべて読み、
    /// ひとつの仮想ルートの下にぶら下げて返す。
    /// devicetree は同じノードを何度も開いて書き足せるが、
    /// こちらは検索しかしないので統合はせず並べておく。
    /// </summary>
    public static DtsNode Parse(string text)
    {
        var parser = new DtsParser(text);
        var root = new DtsNode { Name = "" };

        parser.SkipTrivia();

        while (!parser.AtEnd)
        {
            var node = parser.ParseTopLevel();
            if (node is null) break;

            node.Parent = root;
            root.Children.Add(node);
            parser.SkipTrivia();
        }

        return root;
    }

    private bool AtEnd => _pos >= _text.Length;

    private char Current => _text[_pos];

    private DtsNode? ParseTopLevel()
    {
        if (AtEnd) return null;

        if (Current == '/')
        {
            // "/dts-v1/;" のような版指定や /delete-node/ は読み飛ばす。
            if (LooksLikeSlashDirective())
            {
                SkipToSemicolon();
                return null;
            }

            Advance();
            SkipTrivia();
            var root = ParseNodeBody(new DtsNode { Name = "/" });
            ExpectSemicolon();
            return root;
        }

        if (Current == '&')
        {
            Advance();
            var label = ReadName();
            SkipTrivia();

            var node = ParseNodeBody(new DtsNode { Name = label, Label = label, IsOverride = true });
            ExpectSemicolon();
            return node;
        }

        // 想定外のトークン。詰まらないよう 1 文字進める。
        Advance();
        return null;
    }

    /// <summary><c>/dts-v1/</c> や <c>/delete-node/</c> のような形か。</summary>
    private bool LooksLikeSlashDirective()
    {
        for (var i = _pos + 1; i < _text.Length; i++)
        {
            if (_text[i] == '/') return true;
            if (!char.IsLetterOrDigit(_text[i]) && _text[i] != '-') return false;
        }

        return false;
    }

    private DtsNode ParseNodeBody(DtsNode node)
    {
        Expect('{');
        SkipTrivia();

        while (!AtEnd && Current != '}')
        {
            ParseMember(node);
            SkipTrivia();
        }

        Expect('}');
        return node;
    }

    private void ParseMember(DtsNode parent)
    {
        // "/delete-property/ foo;" などはまとめて捨てる。
        if (Current == '/')
        {
            SkipToSemicolon();
            return;
        }

        string? label = null;
        var name = ReadName();
        SkipTrivia();

        if (!AtEnd && Current == ':')
        {
            label = name;
            Advance();
            SkipTrivia();
            name = ReadName();
            SkipTrivia();
        }

        if (AtEnd) throw new DtsParseException($"'{name}' の後で入力が尽きました。", _line);

        switch (Current)
        {
            case '{':
            {
                var child = ParseNodeBody(new DtsNode { Name = name, Label = label });
                child.Parent = parent;
                parent.Children.Add(child);
                ExpectSemicolon();
                return;
            }

            case '=':
                Advance();
                parent.Properties[name] = ParsePropertyValue(name);
                return;

            case ';':
                Advance();
                parent.Properties[name] = new DtsProperty { Name = name, IsBoolean = true };
                return;

            default:
                throw new DtsParseException($"'{name}' の後に予期しない文字 '{Current}' があります。", _line);
        }
    }

    private DtsProperty ParsePropertyValue(string name)
    {
        var property = new DtsProperty { Name = name };

        while (true)
        {
            SkipTrivia();
            if (AtEnd) throw new DtsParseException($"プロパティ '{name}' が閉じていません。", _line);

            switch (Current)
            {
                case '"':
                    property.Strings.Add(ReadString());
                    break;

                case '<':
                    property.CellArrays.Add(ReadCellArray());
                    break;

                case '[':
                    // バイト列。今のところ使わないので中身は捨てる。
                    SkipBalanced('[', ']');
                    break;

                default:
                    // &label 単独の参照値（phandle）。
                    if (Current == '&')
                    {
                        Advance();
                        property.CellArrays.Add(new List<DtsCell>
                        {
                            new(DtsCellKind.Reference, ReadName()),
                        });
                        break;
                    }

                    throw new DtsParseException(
                        $"プロパティ '{name}' に予期しない文字 '{Current}' があります。", _line);
            }

            SkipTrivia();

            if (!AtEnd && Current == ',')
            {
                Advance();
                continue;
            }

            ExpectSemicolon();
            return property;
        }
    }

    private List<DtsCell> ReadCellArray()
    {
        Expect('<');

        var cells = new List<DtsCell>();

        while (true)
        {
            SkipTrivia();
            if (AtEnd) throw new DtsParseException("'<' が閉じていません。", _line);

            if (Current == '>')
            {
                Advance();
                return cells;
            }

            if (Current == '&')
            {
                Advance();
                cells.Add(new DtsCell(DtsCellKind.Reference, ReadName()));
                continue;
            }

            if (Current == '(')
            {
                cells.Add(new DtsCell(DtsCellKind.Expression, ReadBalanced('(', ')')));
                continue;
            }

            if (char.IsDigit(Current) || (Current == '-' && _pos + 1 < _text.Length && char.IsDigit(_text[_pos + 1])))
            {
                cells.Add(new DtsCell(DtsCellKind.Number, ReadNumber()));
                continue;
            }

            var name = ReadName();
            if (name.Length == 0)
            {
                // 解釈できない記号。無限ループを避けるため進めて捨てる。
                Advance();
                continue;
            }

            // LS(SEMI) のような関数形は、括弧まで含めてひとつのセルにする。
            // ここで分けてしまうと修飾つきキーコードを復元できなくなる。
            if (!AtEnd && Current == '(')
            {
                cells.Add(new DtsCell(DtsCellKind.Expression, name + ReadBalanced('(', ')')));
                continue;
            }

            cells.Add(new DtsCell(DtsCellKind.Identifier, name));
        }
    }

    // ---- 字句 ----

    private string ReadName()
    {
        var start = _pos;

        while (!AtEnd && IsNameChar(Current)) Advance();

        return _text[start.._pos];
    }

    // ',' を含めているのは、プロパティ名に入ることがあるため
    // （chosen ノードの zmk,physical-layout など）。値の区切りの ',' は
    // 完成した値の直後にしか現れないので、ここで飲み込む心配はない。
    private static bool IsNameChar(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '-' or '#' or '.' or '@' or '+' or '?' or '!' or ',';

    private string ReadNumber()
    {
        var start = _pos;

        if (Current == '-') Advance();

        while (!AtEnd && (char.IsLetterOrDigit(Current) || Current == 'x' || Current == 'X')) Advance();

        return _text[start.._pos];
    }

    private string ReadString()
    {
        Expect('"');

        var sb = new StringBuilder();

        while (!AtEnd && Current != '"')
        {
            if (Current == '\\' && _pos + 1 < _text.Length)
            {
                Advance();
                sb.Append(Current);
                Advance();
                continue;
            }

            sb.Append(Current);
            Advance();
        }

        Expect('"');
        return sb.ToString();
    }

    private string ReadBalanced(char open, char close)
    {
        var start = _pos;
        var depth = 0;

        while (!AtEnd)
        {
            if (Current == open) depth++;
            else if (Current == close)
            {
                depth--;
                if (depth == 0)
                {
                    Advance();
                    return _text[start.._pos];
                }
            }

            Advance();
        }

        throw new DtsParseException($"'{open}' が閉じていません。", _line);
    }

    private void SkipBalanced(char open, char close) => ReadBalanced(open, close);

    private void SkipToSemicolon()
    {
        while (!AtEnd && Current != ';') Advance();
        if (!AtEnd) Advance();
    }

    private void SkipTrivia()
    {
        while (!AtEnd && char.IsWhiteSpace(Current)) Advance();
    }

    private void Expect(char c)
    {
        SkipTrivia();

        if (AtEnd || Current != c)
            throw new DtsParseException(
                $"'{c}' が必要ですが {(AtEnd ? "入力の終わり" : $"'{Current}'")} でした。", _line);

        Advance();
    }

    private void ExpectSemicolon()
    {
        SkipTrivia();

        // 末尾のセミコロンが無くても、木は作れているので進める。
        if (!AtEnd && Current == ';') Advance();
    }

    private void Advance()
    {
        if (_text[_pos] == '\n') _line++;
        _pos++;
    }
}
