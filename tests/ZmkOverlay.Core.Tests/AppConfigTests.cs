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
