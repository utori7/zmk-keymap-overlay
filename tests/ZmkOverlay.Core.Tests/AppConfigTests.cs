using ZmkOverlay.Core.Config;

namespace ZmkOverlay.Core.Tests;

public class AppConfigTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), "zmk-config-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void AlwaysIsTheDefaultDisplayMode()
    {
        // 合図を送らない人のオーバーレイが、一度も出ないまま終わらないように。
        var config = new AppConfig();

        Assert.Equal(DisplayModes.Always, config.DisplayMode);
        Assert.True(config.IsAlwaysVisible);
        Assert.True(config.ShowsLayer(0, baseLayerId: 0));
    }

    [Theory]
    [InlineData("always", true)]
    [InlineData("ALWAYS", true)]
    [InlineData("layersOnly", false)]
    [InlineData("selectedLayers", false)]
    [InlineData("", true)]              // 知らない値は既定として扱う
    [InlineData("layers-only", true)]   // 綴り違いで「一度も出ない」にはしない
    public void OnlyTheTwoNamedModesTurnOffAlwaysVisible(string value, bool always)
        => Assert.Equal(always, new AppConfig { DisplayMode = value }.IsAlwaysVisible);

    [Fact]
    public void DisplayModeRoundTripsThroughTheFile()
    {
        new AppConfig { DisplayMode = "always" }.Save(_path);

        Assert.True(AppConfig.Load(_path).IsAlwaysVisible);
    }

    [Theory]
    [InlineData("layersOnly", false, true, true)]
    [InlineData("always", true, true, true)]
    [InlineData("selectedLayers", false, false, true)]
    [InlineData("somethingNew", true, true, true)]   // 知らない値は既定（always）として扱う
    public void EachDisplayModeDecidesWhichLayersShow(string mode, bool baseShown, bool l1Shown, bool l2Shown)
    {
        var config = new AppConfig { DisplayMode = mode, HiddenLayers = new() { 0, 1 } };

        Assert.Equal(baseShown, config.ShowsLayer(0, baseLayerId: 0));
        Assert.Equal(l1Shown, config.ShowsLayer(1, baseLayerId: 0));
        Assert.Equal(l2Shown, config.ShowsLayer(2, baseLayerId: 0));
    }

    [Fact]
    public void SelectedLayersStartsLikeLayersOnly()
    {
        // 初めて「選んだレイヤーのときだけ」にしたとき、何も押していないのに出てくると驚く。
        var config = new AppConfig { DisplayMode = DisplayModes.SelectedLayers };

        Assert.Equal(new[] { 0 }, config.HiddenLayers);
        Assert.False(config.ShowsLayer(0, baseLayerId: 0));
        Assert.True(config.ShowsLayer(1, baseLayerId: 0));
    }

    [Fact]
    public void ShowingEveryLayerSurvivesTheFile()
    {
        // 全部に付けた（空にした）のに、読み直したら既定の [0] に戻る、ということがないように。
        new AppConfig { DisplayMode = DisplayModes.SelectedLayers, HiddenLayers = new() }.Save(_path);

        var loaded = AppConfig.Load(_path);

        Assert.Empty(loaded.HiddenLayers);
        Assert.True(loaded.ShowsLayer(0, baseLayerId: 0));
    }

    [Fact]
    public void HiddenLayersRoundTripThroughTheFile()
    {
        new AppConfig { DisplayMode = DisplayModes.SelectedLayers, HiddenLayers = new() { 0, 3 } }.Save(_path);

        var loaded = AppConfig.Load(_path);

        Assert.Equal(DisplayModes.SelectedLayers, loaded.EffectiveDisplayMode);
        Assert.Equal(new[] { 0, 3 }, loaded.HiddenLayers);
    }

    [Fact]
    public void ComputedPropertiesAreNotWrittenToTheFile()
    {
        // 読み取り専用の派生プロパティまで書き出すと、設定ファイルに
        // 編集しても効かない項目が並ぶ。利用者が手で開くファイルなので出さない。
        new AppConfig().Save(_path);
        var text = File.ReadAllText(_path);

        Assert.DoesNotContain("isHoldMode", text);
        Assert.DoesNotContain("isAlwaysVisible", text);
        Assert.DoesNotContain("isSelectedLayers", text);
        Assert.DoesNotContain("effectiveDisplayMode", text);
        Assert.DoesNotContain("isEnabled", text);
        Assert.DoesNotContain("isInteractive", text);
        Assert.DoesNotContain("hasOffset", text);
        Assert.DoesNotContain("effectiveClickThroughHotkey", text);
    }

    [Fact]
    public void JapaneseIsWrittenAsIsNotAsEscapes()
    {
        // labelOverrides には かな や 英数 が入る。退避表記で書き戻すと
        // 設定ファイルが手で読めなくなる。
        var config = new AppConfig();
        config.Zmk.LabelOverrides["INT4"] = "かな";
        config.Save(_path);

        Assert.Contains("かな", File.ReadAllText(_path));
    }

    [Fact]
    public void SavingKeepsKeysThatTheModelDoesNotKnow()
    {
        // 設定ファイルには _comment のような注釈が書いてある。トレイから
        // 設定を変えて書き戻したときに、それを消してしまわないこと。
        File.WriteAllText(_path, @"{
              ""_comment"": ""これは残っていてほしい"",
              ""keyUnitPx"": 72,
              ""zmk"": {
                ""_signalKeys"": ""対応表の説明"",
                ""keymapFile"": ""a.keymap""
              }
            }");

        var config = AppConfig.Load(_path);
        config.DisplayMode = "always";
        config.Save(_path);

        var text = File.ReadAllText(_path);

        Assert.Contains("_comment", text);
        Assert.Contains("これは残っていてほしい", text);
        Assert.Contains("_signalKeys", text);
        Assert.Contains("対応表の説明", text);
        Assert.Contains("always", text);
        Assert.Equal(72, AppConfig.Load(_path).KeyUnitPx);
    }

    [Fact]
    public void CloneIsIndependentAndKeepsUnknownKeys()
    {
        // 設定画面は複製を書き換えて反映を試し、失敗したら複製を捨てる。
        // 入れ子の辞書やホットキーまで元と共有していると、捨てても元が変わってしまう。
        File.WriteAllText(_path, @"{
              ""_comment"": ""複製にも残る"",
              ""keyUnitPx"": 50,
              ""zmk"": { ""signalKeys"": { ""1"": ""F13"" } }
            }");

        var original = AppConfig.Load(_path);
        var copy = original.Clone();

        copy.KeyUnitPx = 30;
        copy.Zmk.SignalKeys["2"] = "F14";
        copy.ToggleHotkey.Key = "J";
        copy.ClickThroughHotkey.Key = "F14";
        copy.OffsetX = 120;

        Assert.Equal(50, original.KeyUnitPx);
        Assert.Single(original.Zmk.SignalKeys);
        Assert.Equal("K", original.ToggleHotkey.Key);
        Assert.Equal("M", original.ClickThroughHotkey.Key);
        Assert.Equal(0, original.OffsetX);

        copy.Save(_path);
        Assert.Contains("複製にも残る", File.ReadAllText(_path));
    }

    [Fact]
    public void LayersUseCtrlAltDigitsUnlessTheUserChoseOtherwise()
    {
        // 数字キーの無い自作キーボードのために、レイヤーごとに組み合わせを変えられる。
        var config = new AppConfig();
        config.LayerHotkeys["2"] = new HotkeySpec { Modifiers = { "Ctrl", "Shift" }, Key = "F2" };
        config.LayerHotkeys["3"] = new HotkeySpec();   // 割り当てを外した

        Assert.Equal("Ctrl+Alt+1", config.ManualLayerHotkey(1)?.ToString());
        Assert.Equal("Ctrl+Shift+F2", config.ManualLayerHotkey(2)?.ToString());
        Assert.Null(config.ManualLayerHotkey(3));
        Assert.Null(config.ManualLayerHotkey(12));   // 1 桁の数字で押せない番号に既定は無い

        config.EnableManualLayerKeys = false;
        Assert.Null(config.ManualLayerHotkey(2));
    }

    [Fact]
    public void LayerHotkeysRoundTripThroughTheFile()
    {
        var config = new AppConfig();
        config.LayerHotkeys["1"] = new HotkeySpec { Modifiers = { "Win", "Alt" }, Key = "Q" };
        config.Save(_path);

        Assert.Equal("Win+Alt+Q", AppConfig.Load(_path).ManualLayerHotkey(1)?.ToString());
    }

    [Fact]
    public void ClicksPassThroughUnlessTheFileSaysOtherwise()
    {
        // すでに使っている人の config.json にはこのキーが無い。
        // 読んだときに素通しのままでないと、ある日突然オーバーレイがクリックを食べ始める。
        File.WriteAllText(_path, "{}");

        Assert.True(AppConfig.Load(_path).ClickThrough);
        Assert.False(AppConfig.Load(_path).IsInteractive);
    }

    [Fact]
    public void ClickThroughRoundTripsThroughTheFile()
    {
        new AppConfig { ClickThrough = false }.Save(_path);

        Assert.True(AppConfig.Load(_path).IsInteractive);
    }

    [Fact]
    public void AShortcutSwitchesClickThroughFromTheStart()
    {
        // 既定が無いと、オーバーレイを触れるようにする手段が設定画面の中だけになる。
        Assert.Equal("Ctrl+Alt+M", new AppConfig().EffectiveClickThroughHotkey?.ToString());
    }

    [Fact]
    public void ClearingTheClickThroughShortcutLeavesNone()
    {
        // 入力欄で Delete を押したときの形。有効 / 無効のキーと違って外せる。
        var config = new AppConfig { ClickThroughHotkey = new HotkeySpec() };

        Assert.Null(config.EffectiveClickThroughHotkey);
        Assert.Equal("", config.ClickThroughHotkey.ToString());
    }

    [Fact]
    public void TheTwoOverlayShortcutDefaultsNeverCollide()
    {
        // 同じ組み合わせを 2 か所に登録すると、後から登録したほうが黙って効かなくなる。
        // どちらの候補も、レイヤーの既定（Ctrl+Alt+0〜9）と合図キー（F13〜F24）も避けること。
        foreach (var toggle in HotkeyRules.ToggleCandidates)
        {
            Assert.DoesNotContain(toggle, HotkeyRules.ClickThroughCandidates, SpecComparer.Instance);

            foreach (var spec in HotkeyRules.ClickThroughCandidates.Append(toggle))
            {
                Assert.DoesNotMatch(@"^F(1[3-9]|2[0-4])$", spec.Key);

                for (var layer = 0; layer <= 9; layer++)
                    Assert.False(spec.SameAs(HotkeyRules.DefaultLayerHotkey(layer)!), $"{spec} = L{layer}");
            }
        }
    }

    private sealed class SpecComparer : IEqualityComparer<HotkeySpec>
    {
        public static readonly SpecComparer Instance = new();

        public bool Equals(HotkeySpec? a, HotkeySpec? b) => a is not null && b is not null && a.SameAs(b);
        public int GetHashCode(HotkeySpec spec) => 0;
    }

    [Fact]
    public void ClickThroughHotkeyRoundTripsThroughTheFile()
    {
        // 数字キーの無いキーボードでも割り当てられる。
        var config = new AppConfig
        {
            ClickThroughHotkey = new HotkeySpec { Modifiers = { "Ctrl", "Shift" }, Key = "F13" },
        };

        config.Save(_path);

        Assert.Equal("Ctrl+Shift+F13", AppConfig.Load(_path).EffectiveClickThroughHotkey?.ToString());
    }

    [Fact]
    public void DraggedPositionRoundTripsThroughTheFile()
    {
        new AppConfig { OffsetX = -120, OffsetY = 64 }.Save(_path);

        var loaded = AppConfig.Load(_path);

        Assert.Equal(-120, loaded.OffsetX);
        Assert.Equal(64, loaded.OffsetY);
        Assert.True(loaded.HasOffset);
    }

    [Fact]
    public void NotDraggedUntilTheOffsetIsNotZero()
    {
        // 「ドラッグした位置を捨てる」を出すかどうかの判断に使う。
        Assert.False(new AppConfig().HasOffset);
        Assert.True(new AppConfig { OffsetY = 1 }.HasOffset);
    }

    [Fact]
    public void SameCombinationIgnoresOrderCaseAndSpelling()
    {
        var ctrlAltK = new HotkeySpec { Modifiers = { "Ctrl", "Alt" }, Key = "k" };

        Assert.True(ctrlAltK.SameAs(new HotkeySpec { Modifiers = { "alt", "Control" }, Key = "K" }));
        Assert.False(ctrlAltK.SameAs(new HotkeySpec { Modifiers = { "Ctrl" }, Key = "K" }));
        Assert.False(ctrlAltK.SameAs(new HotkeySpec { Modifiers = { "Ctrl", "Alt" }, Key = "J" }));
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }
}
