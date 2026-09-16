using ZmkOverlay.Core.Dts;
using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.Core.Tests;

/// <summary>
/// 物理レイアウトが見つからないときの推定。正確な配置までは求めず、
/// 行の分かれ方・左右の分かれ目・親指の段の寄せ方が取れていることを確かめる。
/// </summary>
public class LayoutGuesserTests
{
    [Fact]
    public void DemoKeyboardIsGuessedExactly()
    {
        // デモのキーマップは、左右の手のあいだを空け、親指の段を字下げして書いてある。
        // 3 段目は左右を詰めて書いてあるが、偶数個なので真ん中で分かれる。
        var guessed = LayoutGuesser.Guess(Fixtures.DemoKeymapText, 40);
        Assert.NotNull(guessed);

        var real = ZmkKeymapReader.Read(new ZmkReadOptions
        {
            KeymapPath = Fixtures.DemoKeymap,
            PhysicalLayoutPath = Fixtures.DemoShield,
        }).Layout.Keys;

        Assert.Equal(real.Select(k => (k.X, k.Y)), guessed!.Keys.Select(k => (k.X, k.Y)));
    }

    [Fact]
    public void SplitKeyboardIsLaidOutInTwoHalves()
    {
        // Corne のように、左右 6 列 + 親指 3 個ずつ。空白 1 個で詰めて書き、手のあいだだけ広く空けてある。
        const string keymap = @"
/ {
    keymap {
        compatible = ""zmk,keymap"";
        base {
            bindings = <
&kp TAB &kp Q &kp W &kp E &kp R &kp T                   &kp Y &kp U &kp I &kp O &kp P &kp BSPC
&kp LCTRL &kp A &kp S &kp D &kp F &kp G                 &kp H &kp J &kp K &kp L &kp SEMI &kp SQT
&kp LSHFT &kp Z &kp X &kp C &kp V &kp B                 &kp N &kp M &kp COMMA &kp DOT &kp FSLH &kp ESC
                  &kp LGUI &mo 1 &kp SPACE              &kp RET &mo 2 &kp RALT
            >;
        };
    };
};";

        var keys = LayoutGuesser.Guess(keymap, 42)!.Keys;

        Assert.Equal((0, 0), (keys[0].X, keys[0].Y));
        Assert.Equal(700, keys[6].X);      // 右手の最初のキーは、左手 6 列 + 隙間 1u の先
        Assert.Equal(1200, keys[11].X);    // 右端

        // 字下げした親指の段は、左右とも手のあいだ寄りに置く。
        Assert.Equal((300, 300), (keys[36].X, keys[36].Y));
        Assert.Equal(500, keys[38].X);
        Assert.Equal(700, keys[39].X);
        Assert.Equal(900, keys[41].X);
    }

    [Fact]
    public void StockCorneKeymapIsSplitAndThumbsSitInside()
    {
        // ZMK 本体の corne.keymap。手のあいだの空白が 3 文字しかなく、1 段目は TAB の後にも同じ幅の空白がある。
        var keys = LayoutGuesser.Guess(File.ReadAllText(Fixtures.CorneKeymap), 42)!.Keys;

        for (var row = 0; row < 3; row++)
        {
            Assert.Equal(500, keys[row * 12 + 5].X);
            Assert.Equal(700, keys[row * 12 + 6].X);
        }

        Assert.Equal(new[] { 300, 400, 500, 700, 800, 900 }, keys.Skip(36).Select(k => k.X));
    }

    [Fact]
    public void OneWideGapDoesNotMakeAKeyboardSplit()
    {
        // 分割していないキーボードで、1 行だけ桁揃えの空白が広い。たまたまなので分けない。
        const string keymap = @"/ { keymap { compatible = ""zmk,keymap""; base { bindings = <
&kp A &kp B &kp C &kp D &kp E &kp F &kp G &kp H &kp I &kp J
&kp A &kp B &kp C &kp D &kp E   &kp F &kp G &kp H &kp I &kp J
&kp A &kp B &kp C &kp D &kp E &kp F &kp G &kp H &kp I &kp J
>; }; }; };";

        var keys = LayoutGuesser.Guess(keymap, 30)!.Keys;

        Assert.All(new[] { 5, 15, 25 }, i => Assert.Equal(500, keys[i].X));
    }

    [Fact]
    public void IndentedShortRowOfAnUnsplitKeyboardIsCentered()
    {
        const string keymap = @"/ { keymap { compatible = ""zmk,keymap""; base { bindings = <
&kp Q &kp W &kp E &kp R &kp T &kp Y &kp U &kp I
&kp A &kp S &kp D &kp F &kp G &kp H &kp J &kp K
            &kp LALT &kp SPACE &kp RALT
>; }; }; };";

        var keys = LayoutGuesser.Guess(keymap, 19)!.Keys;

        Assert.Equal(new[] { 250, 350, 450 }, keys.Skip(16).Select(k => k.X));
    }

    [Fact]
    public void SensorBindingsAreNotMistakenForTheFirstLayer()
    {
        const string keymap = @"/ { keymap { compatible = ""zmk,keymap""; base {
    sensor-bindings = <&inc_dec_kp C_VOL_UP C_VOL_DN>;
    bindings = <
&kp A &kp B
&kp C &kp D
>; }; }; };";

        var keys = LayoutGuesser.Guess(keymap, 4)!.Keys;

        Assert.Equal(new[] { 0, 0, 100, 100 }, keys.Select(k => k.Y));
    }

    [Fact]
    public void CommentedOutBindingsAreNotCounted()
    {
        const string keymap = @"/ { keymap { compatible = ""zmk,keymap"";
    base { bindings = <
&kp A &kp B   // &kp X
/* &kp Y */ &kp C &kp D
    >; }; }; };";

        var keys = LayoutGuesser.Guess(keymap, 4)!.Keys;

        Assert.Equal(new[] { 0, 0, 100, 100 }, keys.Select(k => k.Y));
    }

    [Fact]
    public void KeymapWrittenOnOneLineIsNotGuessed()
    {
        // 行がキーボードの列を表していないので、推定しても意味のある絵にならない。
        var bindings = string.Join(" ", Enumerable.Repeat("&kp A", 40));
        var keymap = $"/ {{ keymap {{ compatible = \"zmk,keymap\"; base {{ bindings = <{bindings}>; }}; }}; }};";

        Assert.Null(LayoutGuesser.Guess(keymap, 40));
    }

    [Fact]
    public void GuessIsRejectedWhenTheKeyCountDiffers()
    {
        const string keymap = @"/ { keymap { compatible = ""zmk,keymap""; base { bindings = <
&kp A &kp B
&kp C &kp D
>; }; }; };";

        Assert.Null(LayoutGuesser.Guess(keymap, 5));
    }

    [Fact]
    public void MaskingCommentsKeepsEveryPosition()
    {
        const string source = "a // b\n/* c\nd */ e \"// not a comment\"";
        var masked = SourceText.MaskComments(source);

        Assert.Equal(source.Length, masked.Length);
        Assert.Equal(source.IndexOf('e'), masked.IndexOf('e'));
        Assert.Contains("\"// not a comment\"", masked);
        Assert.DoesNotContain("b", masked);
        Assert.Equal(source.Count(ch => ch == '\n'), masked.Count(ch => ch == '\n'));
    }
}
