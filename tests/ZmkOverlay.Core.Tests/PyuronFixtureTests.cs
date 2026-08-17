using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.Core.Tests;

/// <summary>
/// 実際の zmk-config を読み切れることを確かめる。
/// 合成した小さな入力だけでは、HRM・入れ子修飾・JIS 別名・
/// マトリクス変換といった実物の組み合わせを踏めない。
/// </summary>
public class PyuronFixtureTests
{
    private static ZmkReadResult Read(HostLayout layout = HostLayout.Jis) =>
        ZmkKeymapReader.Read(new ZmkReadOptions
        {
            KeymapPath = Fixture("config/Pyuron.keymap"),
            PhysicalLayoutPath = Fixture("config/boards/shields/Pyuron/Pyuron.dtsi"),
            HostLayout = layout,
        });

    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "zmk-config", relative);

    [Fact]
    public void PhysicalLayoutHasFortyKeys()
    {
        var layout = Read().Layout;

        Assert.Equal(40, layout.Keys.Count);
        Assert.Equal("Default", layout.Name);
    }

    [Fact]
    public void FirstAndLastKeyPositionsMatchTheDtsi()
    {
        var keys = Read().Layout.Keys;

        Assert.Equal((0, 0, 100, 100), (keys[0].X, keys[0].Y, keys[0].W, keys[0].H));
        Assert.Equal((1200, 300), (keys[39].X, keys[39].Y));
    }

    [Fact]
    public void EveryLayerCoversEveryKey()
    {
        var result = Read();

        Assert.Equal(6, result.Keymap.Layers.Count);
        Assert.All(result.Keymap.Layers, layer => Assert.Equal(40, layer.Keys.Count));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void LayerNamesComeFromNodeNames()
    {
        var names = Read().Keymap.Layers.Select(l => l.Name).ToList();

        Assert.Equal(new[] { "DEFAULT", "SYM", "NAV", "FUNC", "MOUSE", "SYS" }, names);
    }

    [Fact]
    public void HomeRowModIsSplitIntoHoldAndTap()
    {
        // &hml LGUI A は「タップで A、ホールドで Win」。
        // カスタム hold-tap なので bindings = <&kp>, <&kp> から解く必要がある。
        var key = Read().Keymap.Layers[0].Keys[10];

        Assert.Equal("A", key.Tap);
        Assert.Equal("Win", key.Hold);
        Assert.Equal(KeyKind.Modifier, key.Kind);
    }

    [Fact]
    public void HoldTapWithReversedOrderIsRead()
    {
        // &htp EXCLAMATION JP_YEN は <hold> <tap> の順。
        var key = Read().Keymap.Layers[0].Keys[25];

        Assert.Equal("¥", key.Tap);
        Assert.Equal("!", key.Hold);
    }

    [Fact]
    public void LayerTapShowsTargetLayerOnHold()
    {
        var key = Read().Keymap.Layers[0].Keys[35];

        Assert.Equal("Space", key.Tap);
        Assert.Equal("L2 NAV", key.Hold);
        Assert.Equal(KeyKind.Layer, key.Kind);
    }

    [Fact]
    public void JisAliasesResolveToTheCharacterThatActuallyAppears()
    {
        var sym = Read().Keymap.Layers[1].Keys;

        Assert.Equal("+", sym[4].Tap);    // JP_PLUS  = LS(SEMI)
        Assert.Equal("@", sym[5].Tap);    // JP_AT    = LBKT
        Assert.Equal("\"", sym[16].Tap);  // JP_DQT   = LS(N2)
        Assert.Equal("(", sym[17].Tap);   // JP_LPAR  = LS(N8)
        Assert.Equal("|", sym[9].Tap);    // JP_PIPE  = LS(INT3)
    }

    [Fact]
    public void SameKeymapReadsDifferentlyOnUsLayout()
    {
        Assert.Equal("@", Read(HostLayout.Jis).Keymap.Layers[1].Keys[5].Tap);
        Assert.Equal("[", Read(HostLayout.Us).Keymap.Layers[1].Keys[5].Tap);
    }

    [Fact]
    public void NestedModifiersInNavLayerAreComposed()
    {
        var nav = Read().Keymap.Layers[2].Keys;

        Assert.Equal("^⇧Tab", nav[1].Tap);   // LC(LS(TAB))
        Assert.Equal("Win⇧←", nav[11].Tap);  // LG(LS(LEFT_ARROW))
        Assert.Equal("Alt←", nav[21].Tap);   // LA(LEFT_ARROW)
    }

    [Fact]
    public void SystemBindingsAreRecognised()
    {
        var sys = Read().Keymap.Layers[5].Keys;

        Assert.Equal("BT0", sys[5].Tap);
        Assert.Equal("BT全消", sys[20].Tap);
        Assert.Equal("BOOT", sys[24].Tap);
        Assert.All(new[] { sys[5], sys[20], sys[24] }, k => Assert.Equal(KeyKind.System, k.Kind));
    }

    [Fact]
    public void TransparentKeysAreMarked()
    {
        var mouse = Read().Keymap.Layers[4].Keys;

        Assert.Equal(KeyKind.Transparent, mouse[0].Kind);
        Assert.Equal("M左", mouse[16].Tap);
    }

    [Fact]
    public void CombosAreReadWithTheirLayerScope()
    {
        var combos = Read().Keymap.Combos;

        Assert.Equal(4, combos.Count);

        var capsWord = combos[0];
        Assert.Equal("CapsWd", capsWord.Label);
        Assert.Equal(new[] { 13, 16 }, capsWord.KeyPositions);
        Assert.Empty(capsWord.Layers);   // layers 未指定 = 全レイヤー

        var bracket = combos[2];
        Assert.Equal("[", bracket.Label);
        Assert.Equal(new[] { 0 }, bracket.Layers);
    }

    [Fact]
    public void SignalKeysComeFromOptionsNotFromTheKeymap()
    {
        var result = ZmkKeymapReader.Read(new ZmkReadOptions
        {
            KeymapPath = Fixture("config/Pyuron.keymap"),
            PhysicalLayoutPath = Fixture("config/boards/shields/Pyuron/Pyuron.dtsi"),
            SignalKeys = new Dictionary<int, string> { [1] = "F13", [2] = "F14" },
        });

        Assert.Null(result.Keymap.Layers[0].SignalKey);
        Assert.Equal("F13", result.Keymap.Layers[1].SignalKey);
        Assert.Equal("F14", result.Keymap.Layers[2].SignalKey);
        Assert.Null(result.Keymap.Layers[4].SignalKey);
    }
}
