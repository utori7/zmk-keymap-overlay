namespace ZmkOverlay.Core.Config;

/// <summary>
/// 設定ファイルのせいで起動できなくなるのを防ぐ。
///
/// 常駐アプリは、起動に失敗すると設定画面も初期設定も開けない。利用者に残る手段が
/// 「config.json を探して消す」だけになるので、直せるものはアプリの側で直す。
/// </summary>
public static class ConfigRecovery
{
    /// <summary>
    /// 読めない設定ファイルを脇へ退避する。消さないのは、手で書いた内容を利用者が取り戻せるように。
    /// 戻り値は退避先。
    /// </summary>
    public static string Quarantine(string path, DateTime now)
    {
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full)!;
        var stem = Path.GetFileNameWithoutExtension(full);

        var target = Path.Combine(folder, $"{stem}.broken-{now:yyyyMMdd-HHmmss}.json");
        for (var n = 2; File.Exists(target); n++)
            target = Path.Combine(folder, $"{stem}.broken-{now:yyyyMMdd-HHmmss}-{n}.json");

        File.Move(full, target);
        return target;
    }

    /// <summary>
    /// サンプルを表示する設定で、そのパスがもう無い場所（exe のフォルダを移す前の場所や、前の版のサンプル名）を
    /// 指していたら、いまのサンプルに向け直す。向け直したら true。
    /// </summary>
    public static bool RepairSamplePaths(AppConfig config, string configPath, string exeDirectory)
    {
        if (config.Zmk.IsEnabled) return false;

        if (!ConfigPaths.LooksLikeBundledSample(config.LayoutFile)
            || !ConfigPaths.LooksLikeBundledSample(config.KeymapFile))
            return false;

        if (File.Exists(ConfigPaths.Resolve(configPath, config.LayoutFile))
            && File.Exists(ConfigPaths.Resolve(configPath, config.KeymapFile)))
            return false;

        var (layout, keymap) = ConfigPaths.SampleFiles(configPath, exeDirectory);
        if (layout == config.LayoutFile && keymap == config.KeymapFile) return false;

        config.LayoutFile = layout;
        config.KeymapFile = keymap;
        return true;
    }
}
