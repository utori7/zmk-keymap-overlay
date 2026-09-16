using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.Core.Tests;

/// <summary>
/// docs/examples/pyuron/layer-signal.dtsi を当てたキーマップが、素のキーマップと
/// 同じに見えることを確かめる。
///
/// 合図キーを足したせいでオーバーレイの表示が変わってしまうと、
/// レイヤー連動のために可読性を失うという本末転倒になる。
/// devicetree としての正しさはビルドでしか分からないが、
/// 「表示が変わらない」ことはここで守れる。
/// </summary>
public class LayerSignalDtsiTests : IDisposable
{
    private readonly string _directory;

    public LayerSignalDtsiTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "zmk-signal-" + Guid.NewGuid().ToString("N"));

        var config = Path.Combine(_directory, "config");
        var shield = Path.Combine(config, "boards", "shields", "Pyuron");
        Directory.CreateDirectory(shield);

        File.Copy(Fixture("config/boards/shields/Pyuron/Pyuron.dtsi"),
            Path.Combine(shield, "Pyuron.dtsi"));

        File.Copy(RepositoryFile("docs/examples/pyuron/layer-signal.dtsi"),
            Path.Combine(config, "layer-signal.dtsi"));

        File.WriteAllText(Path.Combine(config, "Pyuron.keymap"), Patch(
            File.ReadAllText(Fixture("config/Pyuron.keymap"))));
    }

    /// <summary>docs/examples/pyuron/Pyuron.keymap.patch と同じ 2 か所を当てる。</summary>
    private static string Patch(string keymap)
    {
        // include はレイヤー番号の #define より後に置く必要がある。
        var patched = keymap.Replace(
            "#define L_SYS    5   // bluetooth / boot     - Kana hold  (pos 38)",
            "#define L_SYS    5   // bluetooth / boot     - Kana hold  (pos 38)\n\n#include \"layer-signal.dtsi\"");

        return patched.Replace(
            "&lt L_SYM INT5  &lt L_NAV SPACE",
            "&lt_sym L_SYM INT5  &lt_nav L_NAV SPACE").Replace(
            "&lt L_FUNC ENTER   &lt L_SYS INT4",
            "&lt_func L_FUNC ENTER   &lt_sys L_SYS INT4");
    }

    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "zmk-config", relative);

    /// <summary>テスト出力からリポジトリ直下へ戻る。</summary>
    private static string RepositoryFile(string relative) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", relative));

    private ZmkReadResult ReadPatched() =>
        ZmkKeymapReader.Read(new ZmkReadOptions
        {
            KeymapPath = Path.Combine(_directory, "config", "Pyuron.keymap"),
            PhysicalLayoutPath = Path.Combine(
                _directory, "config", "boards", "shields", "Pyuron", "Pyuron.dtsi"),
        });

    private static ZmkReadResult ReadOriginal() =>
        ZmkKeymapReader.Read(new ZmkReadOptions
        {
            KeymapPath = Fixture("config/Pyuron.keymap"),
            PhysicalLayoutPath = Fixture("config/boards/shields/Pyuron/Pyuron.dtsi"),
        });

    [Fact]
    public void PatchedKeymapStillParsesCleanly()
    {
        var result = ReadPatched();

        Assert.Empty(result.Warnings);
        Assert.Equal(6, result.Keymap.Layers.Count);
        Assert.All(result.Keymap.Layers, layer => Assert.Equal(40, layer.Keys.Count));
    }

    [Fact]
    public void SignalMacrosDoNotChangeHowThumbKeysLook()
    {
        var patched = ReadPatched().Keymap.Layers[0].Keys;

        foreach (var (position, hold, tap) in new[]
                 {
                     (34, "L1 SYM", "無変換"),
                     (35, "L2 NAV", "Space"),
                     (37, "L3 FUNC", "Enter"),
                     (38, "L5 SYS", "変換"),
                 })
        {
            Assert.Equal(tap, patched[position].Tap);
            Assert.Equal(hold, patched[position].Hold);
            Assert.Equal(KeyKind.Layer, patched[position].Kind);
        }
    }

    [Fact]
    public void EveryKeyRendersTheSameAsBeforeThePatch()
    {
        var before = ReadOriginal().Keymap;
        var after = ReadPatched().Keymap;

        for (var layer = 0; layer < before.Layers.Count; layer++)
        {
            for (var key = 0; key < before.Layers[layer].Keys.Count; key++)
            {
                var expected = before.Layers[layer].Keys[key];
                var actual = after.Layers[layer].Keys[key];

                Assert.Equal(expected.Tap, actual.Tap);
                Assert.Equal(expected.Hold, actual.Hold);
                Assert.Equal(expected.Kind, actual.Kind);
            }
        }
    }

    [Fact]
    public void SignalKeysAreReadFromTheKeymapItself()
    {
        // 対応表を設定に手で書かなくても、キーマップに仕込んだマクロから分かる。
        var layers = ReadPatched().Keymap.Layers;

        Assert.Null(layers[0].SignalKey);
        Assert.Equal("F13", layers[1].SignalKey);
        Assert.Equal("F14", layers[2].SignalKey);
        Assert.Equal("F15", layers[3].SignalKey);
        Assert.Null(layers[4].SignalKey);   // MOUSE はキー押下で入らないので合図が無い
        Assert.Equal("F16", layers[5].SignalKey);
    }

    [Fact]
    public void ConfiguredSignalKeysOverrideDetectedOnes()
    {
        var layers = ZmkKeymapReader.Read(new ZmkReadOptions
        {
            KeymapPath = Path.Combine(_directory, "config", "Pyuron.keymap"),
            PhysicalLayoutPath = Path.Combine(
                _directory, "config", "boards", "shields", "Pyuron", "Pyuron.dtsi"),
            SignalKeys = new Dictionary<int, string> { [1] = "F20", [2] = "" },
        }).Keymap.Layers;

        Assert.Equal("F20", layers[1].SignalKey);
        Assert.Equal("F13", layers[1].DetectedSignalKey);   // 検出結果は上書きしても残る
        Assert.Null(layers[2].SignalKey);                   // 空文字は「追従しない」
        Assert.Equal("F15", layers[3].SignalKey);           // 書いていないレイヤーは検出どおり
    }

    [Fact]
    public void KeymapWithoutSignalMacrosHasNoSignalKeys()
    {
        Assert.All(ReadOriginal().Keymap.Layers, layer => Assert.Null(layer.DetectedSignalKey));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 後始末に失敗してもテスト結果には関係ない。
        }
    }
}
