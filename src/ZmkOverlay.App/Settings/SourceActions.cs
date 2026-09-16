using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ZmkOverlay.App.Text;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Text;
using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.App.Settings;

/// <summary>
/// キーマップの読み込み元を選ぶ操作と、その説明文。設定画面と初期設定の両方が使う。
/// </summary>
internal static class SourceActions
{
    public static string? PickFile(Window owner, string title, string filter, string? folder)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
        };

        if (folder is not null && Directory.Exists(folder)) dialog.InitialDirectory = folder;

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    /// <summary>いまのキーマップのフォルダ。ファイルを選ぶ画面の最初の場所に使う。</summary>
    public static string? KeymapFolder(ISettingsHost host) =>
        host.Config.Zmk.IsEnabled
            ? Path.GetDirectoryName(ConfigPaths.Resolve(host.ConfigPath, host.Config.Zmk.KeymapFile!))
            : null;

    /// <summary>
    /// PC の .keymap を選んで読み込む。取り消したら Applied は false で、Error は null。
    ///
    /// キーの並びは自動で探し、見つからなければ推定する。推定もできなければ、
    /// build.yaml からキーボードが分かるときは ZMK 本体から取るか尋ね（通信は利用者が「はい」を選んだときだけ）、
    /// そうでなければキーの並びが書かれた .dtsi を選んでもらう。
    /// </summary>
    public static async Task<(bool Applied, string? Error)> UseLocalKeymapAsync(Window owner, ISettingsHost host)
    {
        var keymap = PickFile(owner, UiText.ChooseKeymap, UiText.KeymapFilter, KeymapFolder(host));
        if (keymap is null) return (false, null);

        // 別のキーボードに切り替えることもあるので、前のキーの並びは引き継がない。
        var next = host.Config.Clone();
        next.Zmk.KeymapFile = keymap;
        next.Zmk.PhysicalLayoutFile = null;
        next.Zmk.ShieldLayoutFolder = null;
        next.Zmk.Shield = null;
        next.Zmk.Source = "local";   // GitHub から読んでいた場合も、PC のファイルに切り替える

        if (host.TryApply(next, out var error, reloadKeymap: true)) return (true, null);
        if (error != Strings.PhysicalLayoutMissing) return (false, error);

        if (ZmkShieldSync.SuggestShield(next, host.ConfigPath) is { } shield)
        {
            var answer = MessageBox.Show(owner, UiText.LayoutNeededAskZmk(shield), owner.Title,
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (answer == MessageBoxResult.Cancel) return (false, null);

            if (answer == MessageBoxResult.Yes)
            {
                try
                {
                    await ZmkShieldSync.FetchAsync(next, host.ConfigPath, host.GitHub, shield);
                }
                catch (GitHubSourceException ex)
                {
                    return (false, ex.Message);
                }

                return host.TryApply(next, out error, reloadKeymap: true) ? (true, null) : (false, error);
            }
        }
        else
        {
            MessageBox.Show(owner, UiText.LayoutNeeded, owner.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        var layout = PickFile(owner, UiText.ChooseLayout, UiText.LayoutFilter, Path.GetDirectoryName(keymap));
        if (layout is null) return (false, null);

        next.Zmk.PhysicalLayoutFile = layout;
        return host.TryApply(next, out error, reloadKeymap: true) ? (true, null) : (false, error);
    }

    /// <summary>
    /// 利用者が「ZMK 本体から取得」を押した。取れて使えたら null、そうでなければ理由。
    /// 通信に失敗したときは <see cref="GitHubSourceException"/> がそのまま出る（呼び出し側が表示する）。
    /// </summary>
    public static async Task<string?> FetchZmkLayoutAsync(ISettingsHost host, string? shield)
    {
        var next = host.Config.Clone();
        next.Zmk.PhysicalLayoutFile = null;   // 取ってきたものを使うため、手で選んだファイルは外す

        await ZmkShieldSync.FetchAsync(next, host.ConfigPath, host.GitHub, shield);

        if (!host.TryApply(next, out var error, reloadKeymap: true)) return error;

        // キーマップの近くに別の定義があるか、キーの数が合わなければ、取ってきたものは使われない。
        return host.LayoutSource == LayoutSource.ZmkRepository ? null : UiText.ZmkFetchedButNotUsed;
    }

    /// <summary>
    /// 同梱のサンプル（Corne）を使う。設定ファイルが exe の隣なら相対パス、
    /// %APPDATA% にあるなら exe の隣の data を絶対パスで指す。
    /// </summary>
    public static bool UseSample(ISettingsHost host, out string? error)
    {
        var (layout, keymap) = ConfigPaths.SampleFiles(host.ConfigPath);

        var next = host.Config.Clone();
        next.Zmk.KeymapFile = null;
        next.Zmk.PhysicalLayoutFile = null;
        next.Zmk.ShieldLayoutFolder = null;
        next.Zmk.Shield = null;
        next.Zmk.Source = "local";
        next.LayoutFile = layout;
        next.KeymapFile = keymap;

        return host.TryApply(next, out error, reloadKeymap: true);
    }

    /// <summary>いまの読み込み元。</summary>
    public static string SourceDescription(ISettingsHost host)
    {
        var zmk = host.Config.Zmk;

        if (zmk.IsGitHub) return UiText.GitHubInUse(zmk.GitHub.Repository, zmk.GitHub.Branch, zmk.GitHub.KeymapPath ?? "");
        if (zmk.IsEnabled) return ConfigPaths.Resolve(host.ConfigPath, zmk.KeymapFile!);

        return UiText.SampleKeymap;
    }

    /// <summary>
    /// 物理レイアウトをどこから得たか。推定だと分からないまま使うと、配置の違いに気づけない。
    /// </summary>
    public static string LayoutDescription(ISettingsHost host)
    {
        var zmk = host.Config.Zmk;

        if (!zmk.IsEnabled) return UiText.SampleKeymap;
        if (!string.IsNullOrWhiteSpace(zmk.PhysicalLayoutFile))
            return ConfigPaths.Resolve(host.ConfigPath, zmk.PhysicalLayoutFile);

        return host.LayoutSource switch
        {
            LayoutSource.Keymap => UiText.LayoutInKeymap,
            LayoutSource.FoundNearby => UiText.LayoutFoundNearby(host.LayoutPath ?? ""),
            LayoutSource.ZmkRepository => UiText.LayoutFromZmk(zmk.Shield),
            LayoutSource.Guessed => UiText.LayoutGuessedLabel,
            _ => host.LayoutPath ?? "",
        };
    }

    /// <summary>既定のブラウザで開く。</summary>
    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
