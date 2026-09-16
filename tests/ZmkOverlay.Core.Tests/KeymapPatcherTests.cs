using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.Core.Tests;

/// <summary>
/// 変更済みキーマップの生成。devicetree としてビルドが通るかはここでは分からないので、
/// 「読み直すと合図キーが見つかり、見た目は元と同じ」「何度かけても同じ」「元に戻せる」を守る。
/// 生成する定義の形は、実機で成立を確かめた zmk/layer-signal.dtsi と同じにしてある。
/// </summary>
public class KeymapPatcherTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "zmk-patch-" + Guid.NewGuid().ToString("N"));

    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "zmk-config", relative);

    /// <summary>テスト出力からリポジトリ直下へ戻る。</summary>
    private static string RepositoryFile(string relative) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relative));

    private static string PyuronSource => File.ReadAllText(Fixture("config/Pyuron.keymap"));

    private static KeymapPatch PatchPyuron() => KeymapPatcher.Patch(PyuronSource, Fixture("config/Pyuron.keymap"));

    /// <summary>zmk-config と同じ並びで一時フォルダに置き、アプリと同じ読み込みで読む。</summary>
    private ZmkReadResult ReadAsConfig(string keymapText, params string[] extraFiles)
    {
        var config = Path.Combine(_directory, "config");
        var shield = Path.Combine(config, "boards", "shields", "Pyuron");
        Directory.CreateDirectory(shield);

        File.Copy(Fixture("config/boards/shields/Pyuron/Pyuron.dtsi"), Path.Combine(shield, "Pyuron.dtsi"), overwrite: true);
        foreach (var file in extraFiles)
            File.Copy(RepositoryFile(file), Path.Combine(config, Path.GetFileName(file)), overwrite: true);

        var keymap = Path.Combine(config, "Pyuron.keymap");
        File.WriteAllText(keymap, keymapText);

        return ZmkKeymapReader.Read(new ZmkReadOptions { KeymapPath = keymap });
    }

    [Fact]
    public void PyuronThumbLayerKeysGetSignalKeys()
    {
        var patch = PatchPyuron();

        Assert.True(patch.Changed);
        Assert.Equal(
            new[] { (1, "F13"), (2, "F14"), (3, "F15"), (5, "F16") },
            patch.Added.Select(a => (a.Layer, a.SignalKey)));

        // L4 MOUSE はトラックボールで自動的に入るので、キーが無い。
        Assert.Equal(new[] { new SkippedLayer(4, SkipReason.NoEntryKey) }, patch.Skipped);

        // ビヘイビア名だけが変わり、引数や桁揃えの空白はそのまま。
        Assert.Contains("&zo_lt_l1 L_SYM INT5  &zo_lt_l2 L_NAV SPACE", patch.Text);
        Assert.DoesNotContain("&lt L_", patch.Text);
    }

    [Fact]
    public void PatchedKeymapReadsBackWithSignalsAndLooksTheSame()
    {
        var before = ReadAsConfig(PyuronSource).Keymap;
        var after = ReadAsConfig(PatchPyuron().Text);

        Assert.Empty(after.Warnings);
        Assert.Equal(
            new[] { null, "F13", "F14", "F15", null, "F16" },
            after.Keymap.Layers.Select(l => l.DetectedSignalKey));

        for (var layer = 0; layer < before.Layers.Count; layer++)
        {
            for (var key = 0; key < before.Layers[layer].Keys.Count; key++)
            {
                var expected = before.Layers[layer].Keys[key];
                var actual = after.Keymap.Layers[layer].Keys[key];

                Assert.Equal(expected.Tap, actual.Tap);
                Assert.Equal(expected.Hold, actual.Hold);
                Assert.Equal(expected.Kind, actual.Kind);
            }
        }
    }

    [Fact]
    public void PatchingTwiceGivesTheSameText()
    {
        var once = PatchPyuron();
        var twice = KeymapPatcher.Patch(once.Text, Fixture("config/Pyuron.keymap"));

        Assert.Equal(once.Text, twice.Text);
        Assert.False(twice.Changed);
        Assert.Equal(once.Added, twice.Added);
    }

    [Fact]
    public void RemovingTheGeneratedPartRestoresTheOriginal()
    {
        Assert.Equal(PyuronSource, KeymapPatcher.RemoveGenerated(PatchPyuron().Text));
    }

    [Fact]
    public void KeymapThatAlreadyHasSignalKeysIsLeftAlone()
    {
        // 手順書どおり layer-signal.dtsi を手で入れた Pyuron。
        var manual = PyuronSource
            .Replace(
                "#define L_SYS    5   // bluetooth / boot     - Kana hold  (pos 38)",
                "#define L_SYS    5   // bluetooth / boot     - Kana hold  (pos 38)\n\n#include \"layer-signal.dtsi\"")
            .Replace("&lt L_SYM INT5  &lt L_NAV SPACE", "&lt_sym L_SYM INT5  &lt_nav L_NAV SPACE")
            .Replace("&lt L_FUNC ENTER   &lt L_SYS INT4", "&lt_func L_FUNC ENTER   &lt_sys L_SYS INT4");

        ReadAsConfig(manual, "zmk/layer-signal.dtsi");
        var patch = KeymapPatcher.Patch(manual, Path.Combine(_directory, "config", "Pyuron.keymap"));

        Assert.False(patch.Changed);
        Assert.Empty(patch.Added);
        Assert.Equal("F13", patch.AlreadySignaled[1]);
        Assert.Equal("F16", patch.AlreadySignaled[5]);
    }

    private const string SmallKeymap = """
        #include <behaviors.dtsi>
        #include <dt-bindings/zmk/keys.h>

        / {
            keymap {
                compatible = "zmk,keymap";

                base {
                    bindings = <
        &kp F13  &mo 1  &tog 2  &kp A   // &mo 3 in a comment is not a key
                    >;
                };

                lower { bindings = <&trans &trans &trans &kp B>; };
                raise { bindings = <&trans &trans &trans &kp C>; };
            };
        };
        """;

    [Fact]
    public void MomentaryLayerGetsAMacroAndUsedSignalKeysAreAvoided()
    {
        var patch = KeymapPatcher.Patch(SmallKeymap, Path.Combine(_directory, "small.keymap"));

        // F13 はキーマップで普通のキーとして使われているので、次の F14 を使う。
        Assert.Equal(new[] { (1, "F14") }, patch.Added.Select(a => (a.Layer, a.SignalKey)));
        Assert.Contains("&zo_mo_l1 1", patch.Text);
        Assert.Contains("zo_mo_l1: zo_mo_l1", patch.Text);
        Assert.DoesNotContain("zo_lt_l", patch.Text);   // &lt が無ければ hold-tap は作らない

        Assert.Contains("// &mo 3 in a comment is not a key", patch.Text);
        Assert.Contains("&kp F13  &zo_mo_l1 1  &tog 2", patch.Text);
        Assert.Equal(new[] { new SkippedLayer(2, SkipReason.ToggleOrOneShot) }, patch.Skipped);

        // 定義は #include の後、最初のルートノードの前。
        Assert.True(patch.Text.IndexOf(KeymapPatcher.BeginMarker) > patch.Text.IndexOf("keys.h"));
        Assert.True(patch.Text.IndexOf(KeymapPatcher.EndMarker) < patch.Text.IndexOf("keymap {"));
    }

    [Fact]
    public void LayerTapKeepsTheBuiltinTimingAndTheKeymapsOverride()
    {
        const string keymap = """
            #include <behaviors.dtsi>
            #include <dt-bindings/zmk/keys.h>

            &lt {
                tapping-term-ms = <250>;   // slower on purpose
            };

            / {
                keymap {
                    compatible = "zmk,keymap";
                    base { bindings = <&lt 1 SPACE &kp A>; };
                    nav { bindings = <&trans &kp LEFT>; };
                };
            };
            """;

        var text = KeymapPatcher.Patch(keymap, Path.Combine(_directory, "lt.keymap")).Text;

        var block = text[text.IndexOf(KeymapPatcher.BeginMarker)..text.IndexOf(KeymapPatcher.EndMarker)];

        Assert.Contains("flavor = \"tap-preferred\";", block);          // ZMK 組み込みの &lt と同じ
        Assert.Contains("bindings = <&zo_mo_l1>, <&kp>;", block);

        // 上書きは既定値より後に写す（devicetree では後に書いたものが勝つ）。
        Assert.True(block.IndexOf("<250>") > block.IndexOf("<200>"));
        Assert.Contains("&zo_lt_l1 1 SPACE", text);
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
