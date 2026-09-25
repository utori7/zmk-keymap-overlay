using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;

namespace ZmkOverlay.Core.Tests;

/// <summary>
/// 設定ファイルのせいで起動できなくなる状況からの復旧と、同梱サンプルの場所。
/// </summary>
public class ConfigRecoveryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "zmk-recovery-" + Guid.NewGuid().ToString("N"));

    private string ExeFolder => Path.Combine(_directory, "ZmkOverlay");

    private string ConfigNextToExe => Path.Combine(ExeFolder, "config.json");

    public ConfigRecoveryTests()
    {
        // 配布物と同じく、exe の隣に data/ がある。
        foreach (var sample in new[] { ConfigPaths.SampleLayoutFile, ConfigPaths.SampleKeymapFile })
        {
            var path = Path.Combine(ExeFolder, sample);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{}");
        }
    }

    [Fact]
    public void SampleIsRelativeNextToTheExeAndAbsoluteElsewhere()
    {
        Assert.Equal(
            (ConfigPaths.SampleLayoutFile, ConfigPaths.SampleKeymapFile),
            ConfigPaths.SampleFiles(ConfigNextToExe, ExeFolder + Path.DirectorySeparatorChar));

        var roaming = ConfigPaths.SampleFiles(Path.Combine(_directory, "AppData", "config.json"), ExeFolder);
        Assert.Equal(Path.Combine(ExeFolder, "data", "layouts", "corne.json"), roaming.Layout);
        Assert.Equal(Path.Combine(ExeFolder, "data", "keymaps", "corne.json"), roaming.Keymap);
    }

    [Theory]
    [InlineData("data/layouts/corne.json", true)]
    [InlineData("data/keymaps/pyuron.json", true)]
    [InlineData(@"C:\Users\me\Downloads\ZmkOverlay-0.1.0\data\layouts\corne.json", true)]
    [InlineData("config/corne.keymap", false)]
    [InlineData("layouts/corne.json", false)]
    [InlineData("", false)]
    public void BundledSamplePathsAreRecognised(string path, bool expected)
    {
        Assert.Equal(expected, ConfigPaths.LooksLikeBundledSample(path));
    }

    [Fact]
    public void SamplePathsFromBeforeTheFolderWasMovedAreRepaired()
    {
        // 初期設定で「サンプル」を選んだあと、フォルダをダウンロードからドキュメントへ移した。
        var config = new AppConfig
        {
            LayoutFile = @"C:\Users\me\Downloads\ZmkOverlay\data\layouts\corne.json",
            KeymapFile = @"C:\Users\me\Downloads\ZmkOverlay\data\keymaps\corne.json",
        };

        Assert.True(ConfigRecovery.RepairSamplePaths(config, ConfigNextToExe, ExeFolder));
        Assert.Equal(ConfigPaths.SampleLayoutFile, config.LayoutFile);
        Assert.Equal(ConfigPaths.SampleKeymapFile, config.KeymapFile);
    }

    [Fact]
    public void SampleFromAnOlderVersionIsRepaired()
    {
        var config = new AppConfig { LayoutFile = "data/layouts/pyuron.json", KeymapFile = "data/keymaps/pyuron.json" };

        Assert.True(ConfigRecovery.RepairSamplePaths(config, ConfigNextToExe, ExeFolder));
        Assert.Equal(ConfigPaths.SampleKeymapFile, config.KeymapFile);
    }

    [Fact]
    public void WorkingOrUserChosenPathsAreLeftAlone()
    {
        var sample = new AppConfig();
        Assert.False(ConfigRecovery.RepairSamplePaths(sample, ConfigNextToExe, ExeFolder));

        var ownJson = new AppConfig { LayoutFile = "mine/layout.json", KeymapFile = "mine/keymap.json" };
        Assert.False(ConfigRecovery.RepairSamplePaths(ownJson, ConfigNextToExe, ExeFolder));

        var zmk = new AppConfig { LayoutFile = "data/layouts/gone.json" };
        zmk.Zmk.KeymapFile = "config/corne.keymap";
        Assert.False(ConfigRecovery.RepairSamplePaths(zmk, ConfigNextToExe, ExeFolder));
        Assert.Equal("data/layouts/gone.json", zmk.LayoutFile);
    }

    [Fact]
    public void BrokenConfigIsMovedAsideNotDeleted()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "config.json");
        var now = new DateTime(2026, 9, 17, 8, 30, 0);

        File.WriteAllText(path, "{ broken");
        var first = ConfigRecovery.Quarantine(path, now);

        File.WriteAllText(path, "");
        var second = ConfigRecovery.Quarantine(path, now);

        Assert.False(File.Exists(path));
        Assert.Equal(Path.Combine(_directory, "config.broken-20260917-083000.json"), first);
        Assert.Equal(Path.Combine(_directory, "config.broken-20260917-083000-2.json"), second);
        Assert.Equal("{ broken", File.ReadAllText(first));
    }

    [Fact]
    public void BundledSampleDataIsReadable()
    {
        // data/ は tools/KeymapDump --json で作る。手で壊していないこと。
        var layout = JsonStore.LoadLayout(Fixtures.RepositoryFile(ConfigPaths.SampleLayoutFile));
        var keymap = JsonStore.LoadKeymap(Fixtures.RepositoryFile(ConfigPaths.SampleKeymapFile));

        JsonStore.Validate(layout, keymap);
        Assert.Equal(42, layout.Keys.Count);
        Assert.Equal(new[] { "Base", "Lower", "Raise", "Adjust" }, keymap.Layers.Select(l => l.Name));
        Assert.Equal(new[] { 0, 1, 2, 3 }, keymap.Layers.Select(l => l.Index));
        Assert.Single(keymap.ConditionalLayers);
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

/// <summary>
/// ショートカットの既定値の決まり。判定の差し込み口は全体で 1 つなので、言語の切り替えと同じく並行させない。
/// </summary>
[Collection(LanguageSwitchingCollection.Name)]
public class HotkeyRulesTests
{
    /// <summary>ドイツ語配列のように、Ctrl+Alt（AltGr）+ 数字と K・M で文字が出る PC のふり。</summary>
    private static bool AltGrLayout(HotkeySpec spec) =>
        spec.Has("ctrl") && spec.Has("alt") && !spec.Has("shift")
        && (spec.Key.Length == 1 && char.IsDigit(spec.Key[0]) || spec.Key is "K" or "M");

    private static void WithRule(Func<HotkeySpec, bool> rule, Action test)
    {
        var previous = HotkeyRules.TypesCharacter;
        HotkeyRules.TypesCharacter = rule;

        try
        {
            test();
        }
        finally
        {
            HotkeyRules.TypesCharacter = previous;
        }
    }

    [Fact]
    public void DefaultLayerShortcutsThatTypeCharactersAreNotUsed() => WithRule(AltGrLayout, () =>
    {
        var config = new AppConfig();
        config.LayerHotkeys["2"] = new HotkeySpec { Modifiers = { "Ctrl", "Alt" }, Key = "2" };

        Assert.Null(config.ManualLayerHotkey(1));                           // 既定は使わない
        Assert.Equal("Ctrl+Alt+2", config.ManualLayerHotkey(2)?.ToString()); // 利用者が選んだものはそのまま
        Assert.True(config.UsesDefaultLayerHotkey(1));
        Assert.False(config.UsesDefaultLayerHotkey(2));
    });

    [Fact]
    public void ToggleDefaultAvoidsCombinationsThatTypeCharacters()
    {
        WithRule(_ => false, () => Assert.Equal("Ctrl+Alt+K", HotkeyRules.ChooseDefaultToggle().ToString()));
        WithRule(AltGrLayout, () => Assert.Equal("Ctrl+Alt+Shift+K", HotkeyRules.ChooseDefaultToggle().ToString()));
        WithRule(spec => spec.Key == "K", () => Assert.Equal("Ctrl+Alt+F12", HotkeyRules.ChooseDefaultToggle().ToString()));
    }

    [Fact]
    public void ClickThroughDefaultAvoidsCombinationsThatTypeCharacters()
    {
        WithRule(_ => false, () => Assert.Equal("Ctrl+Alt+M", HotkeyRules.ChooseDefaultClickThrough().ToString()));
        WithRule(AltGrLayout, () => Assert.Equal("Ctrl+Alt+Shift+M", HotkeyRules.ChooseDefaultClickThrough().ToString()));
        WithRule(spec => spec.Key == "M", () => Assert.Equal("Ctrl+Alt+F11", HotkeyRules.ChooseDefaultClickThrough().ToString()));
    }

    [Fact]
    public void ChosenDefaultIsACopy() => WithRule(_ => false, () =>
    {
        HotkeyRules.ChooseDefaultToggle().Key = "Q";
        HotkeyRules.ChooseDefaultClickThrough().Key = "Q";

        Assert.Equal("K", HotkeyRules.ToggleCandidates[0].Key);
        Assert.Equal("M", HotkeyRules.ClickThroughCandidates[0].Key);
    });
}
