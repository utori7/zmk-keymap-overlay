namespace ZmkOverlay.Core.Model;

/// <summary>
/// ZMK の <c>&amp;key_physical_attrs w h x y r rx ry</c> 1 個ぶん。
/// 単位はすべて 1/100u（100 = キー 1 個ぶん）。ZMK の定義をそのまま持つことで
/// <c>*-layouts.dtsi</c> を無変換で取り込めるようにしてある。
/// </summary>
public sealed class PhysicalKey
{
    public int W { get; set; } = 100;
    public int H { get; set; } = 100;
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>回転角。1/100 度。</summary>
    public int R { get; set; }

    /// <summary>回転中心。</summary>
    public int Rx { get; set; }
    public int Ry { get; set; }
}

public sealed class PhysicalLayout
{
    public string Name { get; set; } = "";

    /// <summary>キー位置。添字がそのまま ZMK の key position。</summary>
    public List<PhysicalKey> Keys { get; set; } = new();

    /// <summary>全キーを含む境界（1/100u）。描画スケールの決定に使う。</summary>
    public (int W, int H) Extent()
    {
        var w = 0;
        var h = 0;
        foreach (var k in Keys)
        {
            w = Math.Max(w, k.X + k.W);
            h = Math.Max(h, k.Y + k.H);
        }
        return (w, h);
    }
}
