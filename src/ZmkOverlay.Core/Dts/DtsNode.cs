namespace ZmkOverlay.Core.Dts;

public enum DtsCellKind
{
    /// <summary><c>&amp;kp</c> のような phandle 参照。</summary>
    Reference,

    Number,

    Identifier,

    /// <summary><c>LS(SEMI)</c> や <c>(A | B)</c> のような括弧つきの式。</summary>
    Expression,
}

public sealed record DtsCell(DtsCellKind Kind, string Text)
{
    public bool IsReference => Kind == DtsCellKind.Reference;

    public override string ToString() => IsReference ? "&" + Text : Text;
}

public sealed class DtsProperty
{
    public string Name { get; init; } = "";

    /// <summary>値を持たない真偽プロパティ（<c>wakeup-source;</c> など）。</summary>
    public bool IsBoolean { get; init; }

    public List<string> Strings { get; } = new();

    /// <summary>
    /// <c>&lt;...&gt;</c> ひとかたまりを 1 要素とする。
    /// <c>keys = &lt;a&gt;, &lt;b&gt;;</c> なら 2 要素になる。
    /// </summary>
    public List<List<DtsCell>> CellArrays { get; } = new();

    /// <summary>すべての <c>&lt;&gt;</c> を連結したセル列。</summary>
    public IEnumerable<DtsCell> AllCells => CellArrays.SelectMany(c => c);

    public string? FirstString => Strings.Count > 0 ? Strings[0] : null;

    public int? FirstNumber
    {
        get
        {
            foreach (var cell in AllCells)
                if (cell.Kind == DtsCellKind.Number && DtsValue.TryParseNumber(cell.Text, out var n))
                    return n;

            return null;
        }
    }
}

public sealed class DtsNode
{
    public string Name { get; init; } = "";

    /// <summary><c>label: name { }</c> の label。</summary>
    public string? Label { get; init; }

    /// <summary><c>&amp;label { }</c> 形式の上書きノードか。</summary>
    public bool IsOverride { get; init; }

    public DtsNode? Parent { get; set; }

    public List<DtsNode> Children { get; } = new();

    public Dictionary<string, DtsProperty> Properties { get; } =
        new(StringComparer.Ordinal);

    public string? Compatible => Properties.TryGetValue("compatible", out var p) ? p.FirstString : null;

    public DtsProperty? Property(string name) =>
        Properties.TryGetValue(name, out var p) ? p : null;

    /// <summary>自分を含む部分木をすべて列挙する。</summary>
    public IEnumerable<DtsNode> Descendants()
    {
        yield return this;

        foreach (var child in Children)
            foreach (var node in child.Descendants())
                yield return node;
    }
}

public static class DtsValue
{
    public static bool TryParseNumber(string text, out int value)
    {
        text = text.Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out value);

        return int.TryParse(text, out value);
    }
}
