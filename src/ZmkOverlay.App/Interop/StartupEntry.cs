using System;
using System.IO;

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
        var exePath = Environment.ProcessPath
                      ?? throw new InvalidOperationException("実行ファイルの場所を特定できません。");

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException(
                            "Windows Script Host を利用できないため、ショートカットを作成できません。");

        dynamic shell = Activator.CreateInstance(shellType)
                        ?? throw new InvalidOperationException("WScript.Shell を生成できません。");

        dynamic link = shell.CreateShortcut(ShortcutPath);

        link.TargetPath = exePath;
        link.Arguments = configPath is null ? "" : $"--config \"{configPath}\"";
        link.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
        link.Description = "ZMK のキーマップを画面に重ねて表示します";
        link.Save();
    }

    public static void Disable()
    {
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
    }
}
