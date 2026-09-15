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

    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "zmk-config", relative);

    [Fact]
    public void ShieldDtsiIsFoundWithoutBeingSpecified()
    {
        // fixture は実物の zmk-config と同じく config/boards/shields/Pyuron/Pyuron.dtsi に置いてある。
        var result = ZmkKeymapReader.Read(new ZmkReadOptions { KeymapPath = Fixture("config/Pyuron.keymap") });

        Assert.Equal(LayoutSource.FoundNearby, result.LayoutSource);
        Assert.EndsWith("Pyuron.dtsi", result.LayoutPath);
        Assert.Equal(40, result.Layout.Keys.Count);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void SpecifiedFileIsUsedFirst()
    {
        var result = ZmkKeymapReader.Read(new ZmkReadOptions
        {
            KeymapPath = Fixture("config/Pyuron.keymap"),
            PhysicalLayoutPath = Fixture("config/boards/shields/Pyuron/Pyuron.dtsi"),
        });

        Assert.Equal(LayoutSource.SpecifiedFile, result.LayoutSource);
    }

    [Fact]
    public void KeymapAloneFallsBackToAGuessAndSaysSo()
    {
        // キーマップだけをダウンロードして持ってきた人の状況。
        Directory.CreateDirectory(_directory);
        var keymap = Path.Combine(_directory, "Pyuron.keymap");
        File.Copy(Fixture("config/Pyuron.keymap"), keymap);

        var result = ZmkKeymapReader.Read(new ZmkReadOptions { KeymapPath = keymap });

        Assert.Equal(LayoutSource.Guessed, result.LayoutSource);
        Assert.Null(result.LayoutPath);
        Assert.Equal(40, result.Layout.Keys.Count);
        Assert.Contains(Strings.LayoutGuessed, result.Warnings);
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
