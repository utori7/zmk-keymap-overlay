using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.Core.Tests;

/// <summary>
/// 変更済みキーマップの生成。devicetree としてビルドが通るかはここでは分からないので、
/// 「読み直すと合図キーが見つかり、見た目は元と同じ」「何度かけても同じ」「元に戻せる」
/// 「書き換え済みなら書き換え済みと分かる」を守る。
/// 生成する定義の形は、実機で成立を確かめた docs/examples/pyuron/layer-signal.dtsi と同じにしてある。
/// </summary>
public class KeymapPatcherTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "zmk-patch-" + Guid.NewGuid().ToString("N"));

    private static KeymapPatch PatchDemo() => KeymapPatcher.Patch(Fixtures.DemoKeymapText, Fixtures.DemoKeymap);

    /// <summary>zmk-config と同じ並びで一時フォルダに置き、アプリと同じ読み込みで読む。</summary>
    private ZmkReadResult ReadAsConfig(string keymapText, params string[] extraFiles)
    {
        var config = Path.Combine(_directory, "config");
        var shield = Path.Combine(config, "boards", "shields", "demo40");
        Directory.CreateDirectory(shield);

        File.Copy(Fixtures.DemoShield, Path.Combine(shield, "demo40.dtsi"), overwrite: true);
        foreach (var file in extraFiles)
            File.Copy(Fixtures.RepositoryFile(file), Path.Combine(config, Path.GetFileName(file)), overwrite: true);

        var keymap = Path.Combine(config, "demo40.keymap");
        File.WriteAllText(keymap, keymapText);

        return ZmkKeymapReader.Read(new ZmkReadOptions { KeymapPath = keymap });
    }

    [Fact]
    public void ThumbLayerKeysGetSignalKeys()
    {
        var patch = PatchDemo();

        Assert.True(patch.Changed);
        Assert.Equal(
            new[] { (1, "F13"), (2, "F16"), (3, "F17"), (5, "F18") },
            patch.Added.Select(a => (a.Layer, a.SignalKey)));
        Assert.Empty(patch.AlreadySignaled);

        // L4 MOUSE はポインティングデバイスで自動的に入るので、キーが無い。
        Assert.Equal(new[] { new SkippedLayer(4, SkipReason.NoEntryKey) }, patch.Skipped);

        // ビヘイビア名だけが変わり、引数や桁揃えの空白はそのまま。
        Assert.Contains("&zo_lt_l1 L_SYM INT5    &zo_lt_l2 L_NAV SPACE", patch.Text);
        Assert.DoesNotContain("&lt L_", patch.Text);
    }

    [Fact]
    public void PatchedKeymapReadsBackWithSignalsAndLooksTheSame()
    {
        var before = ReadAsConfig(Fixtures.DemoKeymapText).Keymap;
        var after = ReadAsConfig(PatchDemo().Text);

        Assert.Empty(after.Warnings);
        Assert.Equal(
            new[] { null, "F13", "F16", "F17", null, "F18" },
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
        var once = PatchDemo();
        var twice = KeymapPatcher.Patch(once.Text, Fixtures.DemoKeymap);

        Assert.Equal(once.Text, twice.Text);
        Assert.False(twice.Changed);
    }

    [Fact]
    public void AlreadyPatchedKeymapIsReportedAsReady()
    {
        // 書き換えを GitHub にコミットして取り直した状態。画面に「書き換えが必要」と出してはいけない。
        var twice = KeymapPatcher.Patch(PatchDemo().Text, Fixtures.DemoKeymap);

        Assert.False(twice.Changed);
        Assert.Empty(twice.Added);
        Assert.Equal(
            new Dictionary<int, string> { [1] = "F13", [2] = "F16", [3] = "F17", [5] = "F18" },
            twice.AlreadySignaled);
        Assert.Equal(new[] { new SkippedLayer(4, SkipReason.NoEntryKey) }, twice.Skipped);
    }

    [Fact]
    public void LayerKeyAddedAfterThePatchIsTheOnlyNewOne()
    {
        // 書き換え済みのキーマップに、あとから L4 に入るキーを足した。
        var patched = PatchDemo().Text.Replace("&kp G ", "&mo L_MOUSE ", StringComparison.Ordinal);
        var again = KeymapPatcher.Patch(patched, Fixtures.DemoKeymap);

        Assert.True(again.Changed);
        Assert.Equal(new[] { (4, "F19") }, again.Added.Select(a => (a.Layer, a.SignalKey)));
        Assert.Equal(new[] { 1, 2, 3, 5 }, again.AlreadySignaled.Keys.Order());
        Assert.Empty(again.Skipped);

        // 変更点は元のファイル（前回の生成分を含む）の行番号で示す。
        var line = Assert.Single(KeymapPatcher.ChangedLines(patched, again.Text));
        Assert.Contains("&mo L_MOUSE", line.Before);
        Assert.Contains("&zo_mo_l4 L_MOUSE", line.After);
        Assert.Contains("&mo L_MOUSE", patched.Replace("\r\n", "\n").Split('\n')[line.Line - 1]);
    }

    [Fact]
    public void SignalKeysFromAnEarlierOrderAreKept()
    {
        // 以前の版は F13 から順に割り当てていた（L2 が F14）。書き込み済みのファームと食い違わないよう、そのまま使う。
        var older = PatchDemo().Text.Replace("F16", "F14", StringComparison.Ordinal);
        var again = KeymapPatcher.Patch(older, Fixtures.DemoKeymap);

        Assert.False(again.Changed);
        Assert.Equal("F14", again.AlreadySignaled[2]);

        // あとから足したレイヤーには、空いている中で先の F16 を使う。
        var added = KeymapPatcher.Patch(older.Replace("&kp G ", "&mo L_MOUSE ", StringComparison.Ordinal), Fixtures.DemoKeymap);
        Assert.Equal(new[] { (4, "F16") }, added.Added.Select(a => (a.Layer, a.SignalKey)));
        Assert.Equal("F14", added.AlreadySignaled[2]);
    }

    [Fact]
    public void KeysThatActOnOtherComputersAreUsedLast()
    {
        // F14 / F15 は macOS の画面の明るさ、F20 / F21 は Linux のマイクとタッチパッド。ほかが尽きたときだけ使う。
        var keymap = SmallKeymap.Replace(
            "&kp F13  &mo 1",
            "&kp F13 &kp F16 &kp F17 &kp F18 &kp F19 &kp F22 &kp F23 &kp F24  &mo 1",
            StringComparison.Ordinal);

        var patch = KeymapPatcher.Patch(keymap, Path.Combine(_directory, "full.keymap"));

        Assert.Equal(new[] { (1, "F14") }, patch.Added.Select(a => (a.Layer, a.SignalKey)));
    }

    [Fact]
    public void RemovingTheGeneratedPartRestoresTheOriginal()
    {
        Assert.Equal(Fixtures.DemoKeymapText, KeymapPatcher.RemoveGenerated(PatchDemo().Text));
    }

    [Fact]
    public void ChangesCanBeShownBeforeReplacingTheFile()
    {
        var patch = PatchDemo();

        // 親指の行だけが変わる。
        var changed = Assert.Single(KeymapPatcher.ChangedLines(Fixtures.DemoKeymapText, patch.Text));
        Assert.Equal(122, changed.Line);
        Assert.Contains("&lt L_SYM INT5", changed.Before);
        Assert.Contains("&zo_lt_l1 L_SYM INT5", changed.After);

        var block = KeymapPatcher.GeneratedBlock(patch.Text);
        Assert.NotNull(block);
        Assert.StartsWith(KeymapPatcher.BeginMarker, block);
        Assert.EndsWith(KeymapPatcher.EndMarker, block);
        Assert.Null(KeymapPatcher.GeneratedBlock(Fixtures.DemoKeymapText));
    }

    [Fact]
    public void GeneratedBehaviorsHaveDisplayNames()
    {
        // ZMK Studio は display-name の無いビヘイビアを一覧に出せない。組み込みの &mo / &lt にも付いている。
        var block = KeymapPatcher.GeneratedBlock(PatchDemo().Text)!;

        Assert.Contains("display-name = \"Momentary Layer + F13\";", block);
        Assert.Contains("display-name = \"Layer-Tap + F18\";", block);
    }

    [Fact]
    public void KeymapThatAlreadyHasSignalKeysIsLeftAlone()
    {
        // 手順書どおり layer-signal.dtsi を手で入れたキーマップ。
        var manual = Fixtures.WithHandMadeSignals(Fixtures.DemoKeymapText);

        ReadAsConfig(manual, "docs/examples/pyuron/layer-signal.dtsi");
        var patch = KeymapPatcher.Patch(manual, Path.Combine(_directory, "config", "demo40.keymap"));

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
                    sensor-bindings = <&mo 2>;
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

        // F13 はキーマップで普通のキーとして使われているので、次の F16 を使う。
        Assert.Equal(new[] { (1, "F16") }, patch.Added.Select(a => (a.Layer, a.SignalKey)));
        Assert.Contains("&zo_mo_l1 1", patch.Text);
        Assert.Contains("zo_mo_l1: zo_mo_l1", patch.Text);
        Assert.DoesNotContain("zo_lt_l", patch.Text);   // &lt が無ければ hold-tap は作らない

        Assert.Contains("// &mo 3 in a comment is not a key", patch.Text);
        Assert.Contains("&kp F13  &zo_mo_l1 1  &tog 2", patch.Text);
        Assert.Contains("sensor-bindings = <&mo 2>", patch.Text);   // レイヤーのキーではない
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

    [Fact]
    public void ConditionalLayerFollowsWhenBothOfItsLayersAreSignaled()
    {
        // Lower + Raise で Adjust。Adjust に入るキーは無いが、合図キーの組み合わせで表示できる。
        var patch = KeymapPatcher.Patch(
            File.ReadAllText(Fixtures.RepositoryFile("tools/sample/corne-demo.keymap")),
            Fixtures.RepositoryFile("tools/sample/corne-demo.keymap"));

        Assert.Equal(new[] { 1, 2 }, patch.Added.Select(a => a.Layer));
        Assert.Equal(new[] { 1, 2 }, patch.Conditional[3]);
        Assert.Empty(patch.Skipped);
    }

    [Fact]
    public void KeymapWithHelperMacrosIsExplained()
    {
        const string keymap = """
            #include "zmk-helpers/helper.h"
            ZMK_LAYER(base, &kp A &mo 1)
            """;

        var error = Assert.Throws<InvalidDataException>(
            () => KeymapPatcher.Patch(keymap, Path.Combine(_directory, "helpers.keymap")));

        Assert.Equal(ZmkOverlay.Core.Text.Strings.KeymapUsesHelperMacros, error.Message);
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
