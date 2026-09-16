using System.Diagnostics;
using System.IO;
using System.Windows;
using ZmkOverlay.App.Text;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Text;

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
    /// PC の .keymap を選んで読み込む。取り消したら false で、error は null。
    ///
    /// 物理レイアウトは自動で探すが、見つからず推定もできなければ、続けてシールドの .dtsi を選んでもらう。
    /// </summary>
    public static bool UseLocalKeymap(Window owner, ISettingsHost host, out string? error)
    {
        error = null;

        var keymap = PickFile(owner, UiText.ChooseKeymap, UiText.KeymapFilter, KeymapFolder(host));
        if (keymap is null) return false;

        // 別のキーボードに切り替えることもあるので、前の物理レイアウトは引き継がない。
        var next = host.Config.Clone();
        next.Zmk.KeymapFile = keymap;
        next.Zmk.PhysicalLayoutFile = null;
        next.Zmk.Source = "local";   // GitHub から読んでいた場合も、PC のファイルに切り替える

        if (host.TryApply(next, out error)) return true;
        if (error != Strings.PhysicalLayoutMissing) return false;

        MessageBox.Show(owner, UiText.LayoutNeeded, owner.Title, MessageBoxButton.OK, MessageBoxImage.Information);

        var layout = PickFile(owner, UiText.ChooseLayout, UiText.LayoutFilter, Path.GetDirectoryName(keymap));
        if (layout is null) return false;

        next.Zmk.PhysicalLayoutFile = layout;
        return host.TryApply(next, out error);
    }

    /// <summary>
    /// 同梱のサンプル（Pyuron）を使う。設定ファイルが exe の隣に無いこともあるので、
    /// exe の隣の data を絶対パスで指す。
    /// </summary>
    public static bool UseSample(ISettingsHost host, out string? error)
    {
        var next = host.Config.Clone();
        next.Zmk.KeymapFile = null;
        next.Zmk.PhysicalLayoutFile = null;
        next.Zmk.Source = "local";
        next.LayoutFile = Path.Combine(ConfigPaths.ExeDirectory, "data", "layouts", "pyuron.json");
        next.KeymapFile = Path.Combine(ConfigPaths.ExeDirectory, "data", "keymaps", "pyuron.json");

        return host.TryApply(next, out error);
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
            LayoutSource.Guessed => UiText.LayoutGuessedLabel,
            _ => host.LayoutPath ?? "",
        };
    }

    /// <summary>既定のブラウザで開く。</summary>
    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
