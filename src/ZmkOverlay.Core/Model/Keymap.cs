namespace ZmkOverlay.Core.Model;

/// <summary>
/// キーの見た目の区別。描画時の色分けにだけ使う。
/// </summary>
public enum KeyKind
{
    Normal,

    /// <summary><c>&amp;trans</c>。下位レイヤーに素通し。</summary>
    Transparent,

    /// <summary><c>&amp;none</c>。</summary>
    None,

    /// <summary>レイヤー切替（<c>&amp;mo</c> / <c>&amp;lt</c> / <c>&amp;tog</c>）。</summary>
    Layer,

    /// <summary>修飾キー、および修飾つきの組み合わせ。</summary>
    Modifier,

    /// <summary>Bluetooth / bootloader など、押すと状態が変わるもの。</summary>
    System,
}

/// <summary>
/// 1 キーの表示内容。tap を中央に大きく、hold を左上に小さく描く。
/// </summary>
public sealed class KeyLabel
{
    public string Tap { get; set; } = "";

    /// <summary>ホールド時の機能。無ければ null。</summary>
    public string? Hold { get; set; }

    public KeyKind Kind { get; set; } = KeyKind.Normal;

    public static KeyLabel Trans() => new() { Tap = "▽", Kind = KeyKind.Transparent };
    public static KeyLabel None() => new() { Tap = "", Kind = KeyKind.None };
}

public sealed class Layer
{
    public int Index { get; set; }
    public string Name { get; set; } = "";

    /// <summary>添字が key position。<see cref="PhysicalLayout.Keys"/> と同じ長さであること。</summary>
    public List<KeyLabel> Keys { get; set; } = new();

    /// <summary>
    /// このレイヤーに入ったことを PC 側へ知らせる合図キー（例 "F13"）。
    /// null なら連動対象外。詳細は DESIGN.md「レイヤー連動（案A）」。
    /// 設定に書かれていればその値、無ければ <see cref="DetectedSignalKey"/>。
    /// </summary>
    public string? SignalKey { get; set; }

    /// <summary>キーマップの中身から読み取れた合図キー。設定で上書きされていても変わらない。</summary>
    public string? DetectedSignalKey { get; set; }
}

public sealed class Combo
{
    public string Label { get; set; } = "";
    public List<int> KeyPositions { get; set; } = new();

    /// <summary>有効レイヤー。空なら全レイヤー。</summary>
    public List<int> Layers { get; set; } = new();
}

public sealed class Keymap
{
    public string Layout { get; set; } = "";
    public List<Layer> Layers { get; set; } = new();
    public List<Combo> Combos { get; set; } = new();
}
