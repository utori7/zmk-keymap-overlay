using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Text;
using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.Core.Tests;

/// <summary>
/// 物理レイアウトを指定しなくても、どこかから得られること。
/// 一般の利用者に「シールドの .dtsi を指定してください」と求めずに済むかどうかがここで決まる。
/// </summary>
public class LayoutResolutionTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "zmk-layout-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ShieldDtsiIsFoundWithoutBeingSpecified()
    {
        // fixture は実際の zmk-config と同じく config/boards/shields/demo40/demo40.dtsi に置いてある。
        var result = ZmkKeymapReader.Read(new ZmkReadOptions { KeymapPath = Fixtures.DemoKeymap });

        Assert.Equal(LayoutSource.FoundNearby, result.LayoutSource);
        Assert.EndsWith("demo40.dtsi", result.LayoutPath);
        Assert.Equal(40, result.Layout.Keys.Count);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void SpecifiedFileIsUsedFirst()
    {
        var result = ZmkKeymapReader.Read(new ZmkReadOptions
        {
            KeymapPath = Fixtures.DemoKeymap,
            PhysicalLayoutPath = Fixtures.DemoShield,
        });

        Assert.Equal(LayoutSource.SpecifiedFile, result.LayoutSource);
    }

    [Fact]
    public void KeymapAloneFallsBackToAGuessAndSaysSo()
    {
        // キーマップだけをダウンロードして持ってきた人の状況。
        var keymap = CopyAlone(Fixtures.DemoKeymap);

        var result = ZmkKeymapReader.Read(new ZmkReadOptions { KeymapPath = keymap });

        Assert.Equal(LayoutSource.Guessed, result.LayoutSource);
        Assert.Null(result.LayoutPath);
        Assert.Equal(40, result.Layout.Keys.Count);
        Assert.Contains(Strings.LayoutGuessed, result.Warnings);
    }

    [Fact]
    public void ShieldFetchedFromZmkIsUsedBeforeGuessing()
    {
        // Corne のシールドは ZMK 本体にある。取ってきた保存分を渡せば、推定せずに正しい配置が出る。
        var keymap = CopyAlone(Fixtures.CorneKeymap);

        var result = ZmkKeymapReader.Read(new ZmkReadOptions
        {
            KeymapPath = keymap,
            ExtraLayoutFolders = new[] { Fixtures.CorneShieldFolder },
        });

        Assert.Equal(LayoutSource.ZmkRepository, result.LayoutSource);
        Assert.Equal("6 Column", result.Layout.Name);
        Assert.Equal(42, result.Layout.Keys.Count);
        Assert.DoesNotContain(Strings.LayoutGuessed, result.Warnings);
    }

    [Fact]
    public void KeymapThatCannotBeGuessedIsRejected()
    {
        Directory.CreateDirectory(_directory);
        var keymap = Path.Combine(_directory, "flat.keymap");
        var bindings = string.Join(" ", Enumerable.Repeat("&kp A", 40));
        File.WriteAllText(keymap, $"/ {{ keymap {{ compatible = \"zmk,keymap\"; base {{ bindings = <{bindings}>; }}; }}; }};");

        var error = Assert.Throws<InvalidDataException>(
            () => ZmkKeymapReader.Read(new ZmkReadOptions { KeymapPath = keymap }));

        Assert.Equal(Strings.PhysicalLayoutMissing, error.Message);
    }

    [Fact]
    public void HelperMacroKeymapIsExplained()
    {
        // zmk-helpers の書き方。マクロの定義は手元に無いので展開できない。
        Directory.CreateDirectory(_directory);
        var keymap = Path.Combine(_directory, "helpers.keymap");
        File.WriteAllText(keymap, """
            #include <behaviors.dtsi>
            #include "zmk-helpers/helper.h"

            ZMK_LAYER(base, &kp A &kp B &mo 1 &kp C)
            ZMK_LAYER(nav, &trans &trans &trans &kp LEFT)
            """);

        var error = Assert.Throws<InvalidDataException>(
            () => ZmkKeymapReader.Read(new ZmkReadOptions { KeymapPath = keymap }));

        Assert.Equal(Strings.KeymapUsesHelperMacros, error.Message);
    }

    private string CopyAlone(string source)
    {
        Directory.CreateDirectory(_directory);
        var copy = Path.Combine(_directory, Path.GetFileName(source));
        File.Copy(source, copy);
        return copy;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 後始末に失敗してもテスト結果には関係ない。
        }
    }
}
