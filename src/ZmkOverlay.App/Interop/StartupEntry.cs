using System;
using System.IO;
using ZmkOverlay.App.Text;

namespace ZmkOverlay.App.Interop;

/// <summary>
/// Windows のサインイン時に自動起動させる。
///
/// 置き場所はスタートアップフォルダ。レジストリの Run キーでも同じことは
/// できるが、このツールは「インストーラもレジストリ書き込みも無し」を
/// 通してきた（会社 PC にユーザー権限だけで入れるため）。
/// ショートカットならただのファイルで、エクスプローラから消せる。
///
/// いずれも管理者権限は要らず、影響範囲はこの利用者だけ。
/// </summary>
internal static class StartupEntry
{
    private const string ShortcutName = "ZMK Keymap Overlay.lnk";

    public static string FolderPath =>
        Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    public static string ShortcutPath => Path.Combine(FolderPath, ShortcutName);

    public static bool IsEnabled => File.Exists(ShortcutPath);

    /// <summary>
    /// 自動起動を登録する。<paramref name="configPath"/> は
    /// <c>--config</c> で明示的に指定して起動したときだけ渡すこと。
    /// 渡さなければ、いつもの探索（exe の隣 → %APPDATA%）に任せる。
    /// </summary>
    public static void Enable(string? configPath)
    {
        var exePath = CurrentExe();
        dynamic link = OpenShortcut();

        link.TargetPath = exePath;
        link.Arguments = configPath is null ? "" : $"--config \"{configPath}\"";
        link.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
        link.Description = UiText.ShortcutDescription;
        link.Save();
    }

    /// <summary>
    /// 登録済みのショートカットが、もう無い exe を指していたら（フォルダごと移した、など）、いまの exe に向け直す。
    /// 参照先がまだあるときは触らない。配布版と開発版を併用している人のショートカットを勝手に書き換えないため。
    /// 失敗しても起動には関係ないので、黙って諦める。
    /// </summary>
    public static void RepairIfMoved()
    {
        try
        {
            if (!IsEnabled) return;

            dynamic link = OpenShortcut();
            string target = link.TargetPath;

            if (string.IsNullOrWhiteSpace(target) || File.Exists(target)) return;

            var exePath = CurrentExe();
            link.TargetPath = exePath;
            link.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
            link.Save();
        }
        catch (Exception)
        {
            // WScript.Shell が使えない環境など。自動起動が効かないだけで、いまの動作には影響しない。
        }
    }

    private static string CurrentExe() =>
        Environment.ProcessPath ?? throw new InvalidOperationException(UiText.ExePathUnknown);

    /// <summary>ショートカットを開く（無ければ新しく作る準備をする）。保存は呼び出し側で。</summary>
    private static dynamic OpenShortcut()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException(UiText.ScriptHostUnavailable);

        dynamic shell = Activator.CreateInstance(shellType)
                        ?? throw new InvalidOperationException(UiText.ScriptHostCreateFailed);

        return shell.CreateShortcut(ShortcutPath);
    }

    public static void Disable()
    {
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
    }
}
