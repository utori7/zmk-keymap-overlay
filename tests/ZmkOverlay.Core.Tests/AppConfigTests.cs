using ZmkOverlay.Core.Config;

namespace ZmkOverlay.Core.Tests;

public class AppConfigTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), "zmk-config-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void LayersOnlyIsTheDefaultDisplayMode()
        => Assert.False(new AppConfig().IsAlwaysVisible);

    [Theory]
    [InlineData("always", true)]
    [InlineData("ALWAYS", true)]
    [InlineData("layersOnly", false)]
    [InlineData("", false)]
    public void AlwaysIsTheOnlyValueThatKeepsItVisible(string value, bool always)
        => Assert.Equal(always, new AppConfig { DisplayMode = value }.IsAlwaysVisible);

    [Fact]
    public void DisplayModeRoundTripsThroughTheFile()
    {
        new AppConfig { DisplayMode = "always" }.Save(_path);

        Assert.True(AppConfig.Load(_path).IsAlwaysVisible);
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
        Assert.DoesNotContain("isEnabled", text);
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

        Assert.Equal(50, original.KeyUnitPx);
        Assert.Single(original.Zmk.SignalKeys);
        Assert.Equal("K", original.ToggleHotkey.Key);

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
