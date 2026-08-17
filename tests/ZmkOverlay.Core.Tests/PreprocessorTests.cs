using ZmkOverlay.Core.Dts;

namespace ZmkOverlay.Core.Tests;

public class PreprocessorTests
{
    private static string Run(string source) => new Preprocessor().ProcessText(source).Text;

    [Fact]
    public void ObjectMacroIsExpanded()
    {
        var text = Run("#define L_SYM 1\nbindings = <&mo L_SYM>;");

        Assert.Contains("&mo 1", text);
    }

    [Fact]
    public void MacroDefinedInTermsOfAnotherIsExpandedFully()
    {
        // Pyuron の JP_* はこの形。1 段で止めると LS(SEMI) が残らず壊れる。
        var text = Run("#define JP_PLUS LS(SEMI)\nbindings = <&kp JP_PLUS>;");

        Assert.Contains("&kp LS(SEMI)", text);
    }

    [Fact]
    public void MultiTokenMacroIsExpanded()
    {
        var text = Run("#define KEYS_R 5 6 7\nhold-trigger-key-positions = <KEYS_R>;");

        Assert.Contains("<5 6 7>", text);
    }

    [Fact]
    public void FunctionLikeMacroSubstitutesArguments()
    {
        var text = Run("#define HM(a, b) &hml a b\nbindings = <HM(LGUI, A)>;");

        Assert.Contains("&hml LGUI A", text);
    }

    [Fact]
    public void SelfReferencingMacroDoesNotHang()
    {
        var text = Run("#define A A B\nvalue = <A>;");

        Assert.Contains("B", text);
    }

    [Fact]
    public void CommentsAreRemovedButLineCountIsKept()
    {
        var source = "#define L_SYM 1   // symbols\n/* block\n   comment */\nvalue = <L_SYM>;";
        var text = Run(source);

        Assert.DoesNotContain("symbols", text);
        Assert.DoesNotContain("block", text);
        Assert.Contains("<1>", text);
        Assert.Equal(source.Count(c => c == '\n'), text.TrimEnd('\n').Count(c => c == '\n'));
    }

    [Fact]
    public void PropertyNamesStartingWithHashAreNotTreatedAsDirectives()
    {
        var text = Run("#binding-cells = <2>;");

        Assert.Contains("#binding-cells", text);
    }

    [Fact]
    public void SystemIncludesAreSkippedWithoutError()
    {
        var result = new Preprocessor().ProcessText("#include <dt-bindings/zmk/keys.h>\nvalue = <1>;");

        Assert.Contains("<1>", result.Text);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void LocalIncludeIsInlined()
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "zmk-pp-" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "extra.dtsi"), "#define FROM_INCLUDE 7");

            var main = Path.Combine(directory.FullName, "main.keymap");
            File.WriteAllText(main, "#include \"extra.dtsi\"\nvalue = <FROM_INCLUDE>;");

            Assert.Contains("<7>", new Preprocessor().ProcessFile(main).Text);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
