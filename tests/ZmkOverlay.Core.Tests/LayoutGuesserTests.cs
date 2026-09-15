using ZmkOverlay.Core.Dts;
using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.Core.Tests;

/// <summary>
/// 物理レイアウトが見つからないときの推定。正確な配置までは求めず、
/// 行の分かれ方と左右の分かれ目が取れていることを確かめる。
/// </summary>
public class LayoutGuesserTests
{
    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "zmk-config", relative);

    [Fact]
    public void PyuronRowsAndHalvesMatchTheRealLayout()
    {
        var guessed = LayoutGuesser.Guess(File.ReadAllText(Fixture("config/Pyuron.keymap")), 40);
        Assert.NotNull(guessed);

        var real = ZmkKeymapReader.Read(new ZmkReadOptions
        {
            KeymapPath = Fixture("config/Pyuron.keymap"),
            PhysicalLayoutPath = Fixture("config/boards/shields/Pyuron/Pyuron.dtsi"),
        }).Layout.Keys;

        // 行の分かれ方は全キーで一致する。
        Assert.Equal(real.Select(k => k.Y), guessed!.Keys.Select(k => k.Y));

        // 親指の行はキーマップに隙間が書かれていないので合わないが、
        // 上の 3 行は左右の分かれ目まで含めて一致する。
        for (var pos = 0; pos < 32; pos++)
            Assert.Equal(real[pos].X, guessed.Keys[pos].X);
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
        Assert.Equal((0, 300), (keys[36].X, keys[36].Y));   // 左の親指は左端から
        Assert.Equal(1000, keys[39].X);    // 右の親指は右端に揃える
        Assert.Equal(1200, keys[41].X);
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
