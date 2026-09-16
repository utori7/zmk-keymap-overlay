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

    /// <summary>
    /// exe のあるフォルダ。配布物は単一 exe なので、アセンブリの場所（Assembly.Location）は使えない
    /// （単一 exe の中では空文字になる）。
    /// </summary>
    public static string ExeDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

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

    /// <summary>
    /// 設定が無いときの新規作成先。exe フォルダが書ければそちら。
    ///
    /// 書けるかどうかは使い捨てのファイルで確かめる。config.json そのものを開いて確かめると、
    /// 中身を書く前に止まったときに空の config.json が残り、次回から起動できなくなる。
    /// </summary>
    public static string DefaultWriteTarget()
    {
        var portable = Path.Combine(ExeDirectory, ConfigFileName);
        var probe = Path.Combine(ExeDirectory, $".zmkoverlay-write-test-{Guid.NewGuid():N}");

        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return portable;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Path.Combine(RoamingDirectory, ConfigFileName);
        }
    }

    /// <summary>同梱サンプルの、exe のフォルダからの場所。</summary>
    public const string SampleLayoutFile = "data/layouts/corne.json";
    public const string SampleKeymapFile = "data/keymaps/corne.json";

    /// <summary>
    /// 設定ファイルに書く同梱サンプルの場所。設定ファイルが exe の隣にあれば相対パスにして、
    /// フォルダごと移しても読めるようにする。%APPDATA% にあるときは相対では指せないので絶対パス。
    /// </summary>
    public static (string Layout, string Keymap) SampleFiles(string configPath) => SampleFiles(configPath, ExeDirectory);

    public static (string Layout, string Keymap) SampleFiles(string configPath, string exeDirectory)
    {
        var configFolder = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? "";

        if (SamePath(configFolder, exeDirectory)) return (SampleLayoutFile, SampleKeymapFile);

        return (Path.Combine(exeDirectory, SampleLayoutFile.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(exeDirectory, SampleKeymapFile.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// 同梱サンプル（exe の隣の data/layouts/*.json と data/keymaps/*.json）を指していそうなパスか。
    /// exe のフォルダを移したあとの古い絶対パスや、前の版のサンプル名も含めて見分ける。
    /// </summary>
    public static bool LooksLikeBundledSample(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 3
               && parts[^3].Equals("data", StringComparison.OrdinalIgnoreCase)
               && (parts[^2].Equals("layouts", StringComparison.OrdinalIgnoreCase)
                   || parts[^2].Equals("keymaps", StringComparison.OrdinalIgnoreCase))
               && parts[^1].EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>設定ファイル中の相対パスを、その設定ファイルからの相対で解決する。</summary>
    public static string Resolve(string configPath, string relative) =>
        Path.IsPathRooted(relative)
            ? relative
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configPath)!, relative));
}
