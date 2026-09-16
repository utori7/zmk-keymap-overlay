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

/// <summary>物理レイアウトをどこから得たか。設定画面で利用者に見せる。</summary>
public enum LayoutSource
{
    /// <summary>設定で指定したファイル。</summary>
    SpecifiedFile,

    /// <summary>キーマップ自身に書かれていた。</summary>
    Keymap,

    /// <summary>キーマップのフォルダ以下で見つけた（シールドの .dtsi など）。</summary>
    FoundNearby,

    /// <summary>ZMK 本体から取ってきたシールドの定義で見つけた（Corne など、定義が ZMK 本体にあるキーボード）。</summary>
    ZmkRepository,

    /// <summary>キーマップの書き方から推定した。正確とは限らない。</summary>
    Guessed,

    /// <summary>手書きの JSON。</summary>
    Json,
}
