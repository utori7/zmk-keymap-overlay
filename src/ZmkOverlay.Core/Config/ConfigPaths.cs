namespace ZmkOverlay.Core.Config;

/// <summary>
/// 設定とデータの探索場所。
///
/// exe と同じフォルダを最優先にしてある（ポータブル動作）。会社 PC のように
/// インストーラを使えない環境で、フォルダごとコピーすれば動く状態を保つため。
/// そこに無ければ %APPDATA%\ZmkOverlay\ にフォールバックする。
/// </summary>
public static class ConfigPaths
{
    public const string AppFolderName = "ZmkOverlay";
    public const string ConfigFileName = "config.json";

    public static string ExeDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath
            ?? System.Reflection.Assembly.GetEntryAssembly()!.Location)!;

    public static string RoamingDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);

    /// <summary>読み込むべき設定ファイル。どちらにも無ければ null。</summary>
    public static string? FindConfig()
    {
        var portable = Path.Combine(ExeDirectory, ConfigFileName);
        if (File.Exists(portable)) return portable;

        var roaming = Path.Combine(RoamingDirectory, ConfigFileName);
        return File.Exists(roaming) ? roaming : null;
    }

    /// <summary>設定が無いときの新規作成先。exe フォルダが書ければそちら。</summary>
    public static string DefaultWriteTarget()
    {
        var portable = Path.Combine(ExeDirectory, ConfigFileName);
        try
        {
            using (File.Open(portable, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite)) { }
            return portable;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Path.Combine(RoamingDirectory, ConfigFileName);
        }
    }

    /// <summary>設定ファイル中の相対パスを、その設定ファイルからの相対で解決する。</summary>
    public static string Resolve(string configPath, string relative) =>
        Path.IsPathRooted(relative)
            ? relative
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configPath)!, relative));
}
