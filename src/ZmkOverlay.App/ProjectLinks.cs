using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ZmkOverlay.Core.Text;

namespace ZmkOverlay.App;

/// <summary>このアプリの公開先と、そこへのリンク。</summary>
internal static class ProjectLinks
{
    public const string Repository = "https://github.com/utori7/zmk-keymap-overlay";

    /// <summary>キーボードの書き換えの結果を報告する Issue フォーム（.github/ISSUE_TEMPLATE/keyboard-update.yml）。</summary>
    private const string KeyboardUpdateTemplate = "keyboard-update.yml";

    private const string Docs = Repository + "/blob/main";

    /// <summary>
    /// 次の 2 つは表示言語に合わせた版を指す。README の慣習どおり、基本の名前が英語で ".ja" が日本語。
    /// 配布物にも同じファイルが入っているが、開発中の実行では隣に無いことがあるので公開先を開く。
    /// </summary>
    public static string Readme =>
        Strings.Language == UiLanguage.Ja ? $"{Docs}/README.ja.md" : $"{Docs}/README.md";

    public static string ConfigurationDoc =>
        Strings.Language == UiLanguage.Ja ? $"{Docs}/docs/configuration.ja.md" : $"{Docs}/docs/configuration.md";

    /// <summary>画面に出すバージョン。</summary>
    public static string AppVersion
    {
        get
        {
            var version = typeof(ProjectLinks).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

            // SDK はビルド時のコミットを "+abcdef" の形で後ろに付ける。画面にはいらない。
            var plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }
    }

    /// <summary>
    /// 書き換えの結果を報告するページ。分かっている値は、フォームの id を使って入力欄に入れておく
    /// （送るかどうかは、開いたページで本人が決める）。入れるのはキーボード名と版だけで、zmk-config の場所は入れない。
    /// </summary>
    public static string KeyboardUpdateReport(string? keyboard, string? zmkVersion)
    {
        var fields = new List<(string Id, string? Value)>
        {
            ("template", KeyboardUpdateTemplate),
            ("app-version", AppVersion),
            ("keyboard", keyboard),
            ("zmk-version", zmkVersion),
        };

        var query = string.Join("&", fields
            .Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .Select(f => $"{f.Id}={Uri.EscapeDataString(f.Value!.Trim())}"));

        return $"{Repository}/issues/new?{query}";
    }
}
