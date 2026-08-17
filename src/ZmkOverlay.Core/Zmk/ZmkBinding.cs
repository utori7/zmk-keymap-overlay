using ZmkOverlay.Core.Dts;

namespace ZmkOverlay.Core.Zmk;

/// <summary><c>&amp;lt L_SYM INT5</c> ひとつぶん。</summary>
public sealed record ZmkBinding(string Behavior, IReadOnlyList<string> Parameters)
{
    public string? Param(int index) =>
        index < Parameters.Count ? Parameters[index] : null;

    public override string ToString() =>
        Parameters.Count == 0 ? "&" + Behavior : "&" + Behavior + " " + string.Join(" ", Parameters);

    /// <summary>
    /// セル列をバインディングに割る。
    ///
    /// 区切りは phandle 参照。ZMK は <c>#binding-cells</c> で引数の数を決めるが、
    /// 「次の <c>&amp;</c> までが引数」と見るだけで同じ結果になり、
    /// 定義を知らないビヘイビアにも効く。
    /// </summary>
    public static List<ZmkBinding> Split(IEnumerable<DtsCell> cells)
    {
        var result = new List<ZmkBinding>();

        string? behavior = null;
        var parameters = new List<string>();

        foreach (var cell in cells)
        {
            if (cell.IsReference)
            {
                if (behavior is not null) result.Add(new ZmkBinding(behavior, parameters));

                behavior = cell.Text;
                parameters = new List<string>();
                continue;
            }

            if (behavior is null) continue;   // 参照より前にある値は捨てる
            parameters.Add(cell.Text);
        }

        if (behavior is not null) result.Add(new ZmkBinding(behavior, parameters));

        return result;
    }
}

/// <summary>キーマップ中で定義されたビヘイビア。組み込みでないものを解決するのに使う。</summary>
public sealed class BehaviorInfo
{
    public string Label { get; init; } = "";
    public string Compatible { get; init; } = "";

    /// <summary><c>#binding-cells</c>。無ければ null。</summary>
    public int? BindingCells { get; init; }

    /// <summary><c>bindings</c> プロパティ。hold-tap なら [hold, tap] の順。</summary>
    public IReadOnlyList<ZmkBinding> Bindings { get; init; } = Array.Empty<ZmkBinding>();

    public bool IsHoldTap => Compatible == "zmk,behavior-hold-tap";

    public bool IsMacro => Compatible.StartsWith("zmk,behavior-macro", StringComparison.Ordinal);
}
