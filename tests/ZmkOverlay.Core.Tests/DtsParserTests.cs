using ZmkOverlay.Core.Dts;

namespace ZmkOverlay.Core.Tests;

public class DtsParserTests
{
    [Fact]
    public void ReadsLabelsAndNestedNodes()
    {
        var root = DtsParser.Parse(@"
            / {
                keymap {
                    compatible = ""zmk,keymap"";
                    default_layer {
                        bindings = <&kp Q &kp W>;
                    };
                };
            };");

        var keymap = root.Descendants().Single(n => n.Compatible == "zmk,keymap");

        Assert.Single(keymap.Children);
        Assert.Equal("default_layer", keymap.Children[0].Name);
    }

    [Fact]
    public void ReadsPropertyNameContainingComma()
    {
        // chosen ノードの zmk,physical-layout がこの形。
        var root = DtsParser.Parse(@"
            / {
                chosen {
                    zmk,physical-layout = &default_layout;
                };
            };");

        var chosen = root.Descendants().Single(n => n.Name == "chosen");

        Assert.True(chosen.Properties.ContainsKey("zmk,physical-layout"));
    }

    [Fact]
    public void KeepsEachAngleBracketGroupSeparate()
    {
        var root = DtsParser.Parse("/ { node { keys = <&a 1 2>, <&a 3 4>; }; };");
        var keys = root.Descendants().Single(n => n.Name == "node").Property("keys")!;

        Assert.Equal(2, keys.CellArrays.Count);
        Assert.Equal(6, keys.AllCells.Count());
    }

    [Fact]
    public void FunctionFormValueStaysOneCell()
    {
        // LS(SEMI) を LS / SEMI に割ってしまうと修飾つきキーを復元できなくなる。
        var root = DtsParser.Parse("/ { node { bindings = <&kp LS(SEMI)>; }; };");
        var cells = root.Descendants().Single(n => n.Name == "node").Property("bindings")!.AllCells.ToList();

        Assert.Equal(2, cells.Count);
        Assert.Equal("LS(SEMI)", cells[1].Text);
        Assert.Equal(DtsCellKind.Expression, cells[1].Kind);
    }

    [Fact]
    public void NestedFunctionFormStaysOneCell()
    {
        var root = DtsParser.Parse("/ { node { bindings = <&kp LC(LS(TAB))>; }; };");
        var cells = root.Descendants().Single(n => n.Name == "node").Property("bindings")!.AllCells.ToList();

        Assert.Equal("LC(LS(TAB))", cells[1].Text);
    }

    [Fact]
    public void ParenthesisedExpressionDoesNotBreakParsing()
    {
        var root = DtsParser.Parse("/ { node { p = <&x (A | B)>, <&y 1>; }; };");
        var property = root.Descendants().Single(n => n.Name == "node").Property("p")!;

        Assert.Equal(2, property.CellArrays.Count);
    }

    [Fact]
    public void BooleanPropertyIsRecorded()
    {
        var root = DtsParser.Parse("/ { node { wakeup-source; }; };");
        var property = root.Descendants().Single(n => n.Name == "node").Property("wakeup-source")!;

        Assert.True(property.IsBoolean);
    }

    [Fact]
    public void OverrideNodeIsParsed()
    {
        var root = DtsParser.Parse("&mt { flavor = \"balanced\"; };");
        var node = root.Descendants().Single(n => n.Name == "mt");

        Assert.True(node.IsOverride);
        Assert.Equal("balanced", node.Property("flavor")!.FirstString);
    }

    [Fact]
    public void NodeNameWithUnitAddressIsParsed()
    {
        var root = DtsParser.Parse("/ { split { trackball: trackball@0 { reg = <0>; }; }; };");

        Assert.Contains(root.Descendants(), n => n.Name == "trackball@0" && n.Label == "trackball");
    }
}
