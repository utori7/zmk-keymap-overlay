using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.Core.Tests;

public class KeycodeTableTests
{
    [Theory]
    [InlineData("A", "A")]
    [InlineData("N1", "1")]
    [InlineData("SPACE", "Space")]
    [InlineData("LEFT_ARROW", "←")]
    [InlineData("F10", "F10")]
    [InlineData("LGUI", "Win")]
    public void CommonKeysAreLabelled(string code, string expected)
        => Assert.Equal(expected, KeycodeTable.Label(code, HostLayout.Jis));

    // ZMK は US 位置の HID コードを送るので、同じ binding でも
    // ホストの配列によって出る文字が変わる。
    [Theory]
    [InlineData("LBKT", "[", "@")]
    [InlineData("EQUAL", "=", "^")]
    [InlineData("SQT", "'", ":")]
    [InlineData("LS(N2)", "@", "\"")]
    [InlineData("LS(N8)", "*", "(")]
    [InlineData("LS(SEMI)", ":", "+")]
    [InlineData("LS(MINUS)", "_", "=")]
    public void LayoutChangesTheLabel(string code, string us, string jis)
    {
        Assert.Equal(us, KeycodeTable.Label(code, HostLayout.Us));
        Assert.Equal(jis, KeycodeTable.Label(code, HostLayout.Jis));
    }

    [Fact]
    public void ShiftedAliasResolvesThroughItsBaseKey()
    {
        // EXCLAMATION は LS(N1)。JIS でも Shift+1 は ! なので US と同じになる。
        Assert.Equal("!", KeycodeTable.Label("EXCLAMATION", HostLayout.Jis));

        // AT は LS(N2)。名前は US 由来だが、JIS では " が出る。
        Assert.Equal("\"", KeycodeTable.Label("AT", HostLayout.Jis));
        Assert.Equal("@", KeycodeTable.Label("AT", HostLayout.Us));
    }

    [Fact]
    public void NestedModifiersAreComposed()
    {
        Assert.Equal("^⇧Tab", KeycodeTable.Label("LC(LS(TAB))", HostLayout.Jis));
        Assert.Equal("Win⇧←", KeycodeTable.Label("LG(LS(LEFT_ARROW))", HostLayout.Jis));
        Assert.Equal("Alt←", KeycodeTable.Label("LA(LEFT_ARROW)", HostLayout.Jis));
    }

    [Fact]
    public void JisOnlyKeysFallBackToTheirNameOnUs()
    {
        Assert.Equal("¥", KeycodeTable.Label("INT3", HostLayout.Jis));
        Assert.Equal("INT3", KeycodeTable.Label("INT3", HostLayout.Us));
    }

    [Fact]
    public void OverridesWin()
    {
        var overrides = new Dictionary<string, string> { ["INT5"] = "英数" };

        Assert.Equal("英数", KeycodeTable.Label("INT5", HostLayout.Jis, overrides));
        Assert.Equal("無変換", KeycodeTable.Label("INT5", HostLayout.Jis));
    }

    [Fact]
    public void KeypadDigitsAreShownAsDigits()
        => Assert.Equal("7", KeycodeTable.Label("KP_NUMBER_7", HostLayout.Jis));

    [Fact]
    public void UnknownKeycodeIsShownAsIs()
    {
        // 表に無いことが画面から分かるほうが、黙って空白になるよりよい。
        Assert.Equal("SOME_NEW_KEY", KeycodeTable.Label("SOME_NEW_KEY", HostLayout.Jis));
    }
}
