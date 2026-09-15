using System.Runtime.CompilerServices;
using ZmkOverlay.Core.Text;
using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.Core.Tests;

/// <summary>
/// 既存のテストは日本語の表示を前提に書いてある。実行する PC の表示言語に
/// 左右されないよう、どのテストよりも先に日本語へ固定する。
/// </summary>
internal static class LanguagePin
{
    [ModuleInitializer]
    internal static void PinJapanese() => Strings.Language = UiLanguage.Ja;
}

/// <summary>
/// 表示言語は全体で 1 つの状態なので、切り替えるテストは他と並行させない。
/// xUnit は並列化を切ったコレクションを、並列のものがすべて終わってから流す。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LanguageSwitchingCollection
{
    public const string Name = "Language switching";
}

[Collection(LanguageSwitchingCollection.Name)]
public class LanguageTests
{
    [Theory]
    [InlineData("ja", UiLanguage.Ja)]
    [InlineData("en", UiLanguage.En)]
    [InlineData(" EN ", UiLanguage.En)]
    public void ExplicitSettingWins(string setting, UiLanguage expected)
    {
        Assert.Equal(expected, Strings.FromSetting(setting));
    }

    [Fact]
    public void KeyNamesFollowTheLanguage()
    {
        try
        {
            Strings.Language = UiLanguage.En;

            Assert.Equal("Muhenkan", KeycodeTable.Label("INT5", HostLayout.Jis));
            Assert.Equal("Bri+", KeycodeTable.Label("C_BRI_UP", HostLayout.Us));

            // 利用者の差し替えは言語より優先する。
            var overrides = new Dictionary<string, string> { ["INT5"] = "英数" };
            Assert.Equal("英数", KeycodeTable.Label("INT5", HostLayout.Jis, overrides));
        }
        finally
        {
            Strings.Language = UiLanguage.Ja;
        }
    }

    [Fact]
    public void MessagesFollowTheLanguage()
    {
        try
        {
            Strings.Language = UiLanguage.En;
            Assert.Equal("Line 3: '<' is not closed.", Strings.AtLine(3, Strings.NotClosed('<')));

            Strings.Language = UiLanguage.Ja;
            Assert.Equal("3 行目: '<' が閉じていません。", Strings.AtLine(3, Strings.NotClosed('<')));
        }
        finally
        {
            Strings.Language = UiLanguage.Ja;
        }
    }
}
