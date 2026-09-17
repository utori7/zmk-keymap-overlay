using System.Text;
using System.Text.RegularExpressions;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Text;

namespace ZmkOverlay.Core.Zmk;

/// <summary>zmk-config の build.yaml と west.yml から分かること。</summary>
public sealed record ZmkBuildInfo(IReadOnlyList<string> Shields, string Revision);

/// <summary>ZMK 本体から取ってきたシールド定義。<paramref name="Folder"/> はシールドのフォルダ。</summary>
public sealed record ZmkShieldResult(string Shield, string Folder);

/// <summary>
/// ZMK 本体（zmkfirmware/zmk）から、シールドのキーの並び（物理レイアウト）を取ってくる。
///
/// Corne・Lily58・Sofle のような定番のキーボードは、シールドの定義が利用者の zmk-config ではなく
/// ZMK 本体にある。キーマップの近くを探しても見つからず、推定では親指の段などが実物と合わない。
/// どのキーボードかは zmk-config の build.yaml に書いてあるので、そこから取りに行く。
///
/// 取るのはシールドのフォルダの .dtsi / .overlay と、それが読み込む共有レイアウト（app/dts/layouts/...）だけ。
/// ZMK のソースと同じ並びで保存するので、<see cref="Dts.Preprocessor"/> が &lt;layouts/...&gt; をそのまま解決できる。
/// 通信は GitHub API を数回（フォルダの一覧）と、本文を raw.githubusercontent.com から。
/// </summary>
public static class ZmkShieldSource
{
    public const string BuildFileName = "build.yaml";
    public const string WestFileName = "west.yml";

    private const string ShieldsPath = "app/boards/shields";

    /// <summary>
    /// 物理レイアウトの定義に現れる文字列。引用符まで含めるのは、chosen の <c>zmk,physical-layout = &amp;x;</c>
    /// （定義ではなく参照）と区別するため。
    /// </summary>
    internal const string PhysicalLayoutDefinition = "\"zmk,physical-layout\"";
    private const int MaxApiCalls = 6;
    private const int MaxFiles = 40;

    private static readonly GitHubLocation Zmk = new("zmkfirmware", "zmk");

    /// <summary>キーボード本体ではなく、表示器や設定用に足すシールド。候補の後ろに回す。</summary>
    private static readonly HashSet<string> AddOns = new(StringComparer.OrdinalIgnoreCase)
    {
        "nice_view", "nice_view_adapter", "nice_view_gem", "nice_oled", "settings_reset",
        "studio-rpc-usb-uart", "zmk_usb_logging", "rgbled_adapter",
    };

    private static readonly string[] SideSuffixes = { "_left", "_right", "_central", "_peripheral" };

    private static readonly Regex LayoutInclude = new(@"^[ \t]*#[ \t]*include[ \t]*<(layouts/[^>]+)>", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex LocalInclude = new(@"^[ \t]*#[ \t]*include[ \t]*""([^""]+)""", RegexOptions.Compiled | RegexOptions.Multiline);

    // ---- build.yaml / west.yml ----

    /// <summary>
    /// キーマップから上へたどって build.yaml を探し、シールド名と ZMK の版を読む。
    /// 見つからなければシールドは空、版は main。
    /// </summary>
    public static ZmkBuildInfo ReadBuildInfo(string keymapPath)
    {
        var keymapFolder = Path.GetDirectoryName(Path.GetFullPath(keymapPath));
        var current = keymapFolder is null ? null : new DirectoryInfo(keymapFolder);

        for (var depth = 0; current is not null && depth < 3; depth++, current = current.Parent)
        {
            var build = Path.Combine(current.FullName, BuildFileName);
            if (!File.Exists(build)) continue;

            var west = new[]
                {
                    Path.Combine(current.FullName, "config", WestFileName),
                    Path.Combine(current.FullName, WestFileName),
                    Path.Combine(keymapFolder!, WestFileName),
                }
                .FirstOrDefault(File.Exists);

            return new ZmkBuildInfo(
                ParseShields(ReadText(build)),
                west is null ? "main" : ParseRevision(ReadText(west)));
        }

        return new ZmkBuildInfo(Array.Empty<string>(), "main");
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>
    /// build.yaml の shield をすべて読む。<c>shield: corne_left nice_view_adapter nice_view</c> のような 1 行の形も、
    /// <c>shield: [a, b]</c> や、次の行から <c>- a</c> と並べる形も受け付ける。
    /// 左右の区別（_left / _right など）は外し、キーボード本体らしいものを先に並べる。
    /// </summary>
    public static IReadOnlyList<string> ParseShields(string buildYaml)
    {
        var names = new List<string>();
        var lines = buildYaml.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = StripComment(lines[i]);
            var match = Regex.Match(line, @"^(\s*)(?:-\s*)?shield\s*:(.*)$");
            if (!match.Success) continue;

            var value = match.Groups[2].Value;

            if (value.Trim().Length > 0)
            {
                names.AddRange(SplitNames(value));
                continue;
            }

            // 次の行から「- 名前」が並ぶ形。
            for (var j = i + 1; j < lines.Length; j++)
            {
                var item = Regex.Match(StripComment(lines[j]), @"^\s*-\s*(\S.*)$");
                if (!item.Success) break;

                names.AddRange(SplitNames(item.Groups[1].Value));
                i = j;
            }
        }

        return names
            .Select(BaseName)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => AddOns.Contains(n) ? 1 : 0)
            .ToList();
    }

    /// <summary>
    /// west.yml で zmk のプロジェクトに書かれた revision。書かれていなければ <c>defaults</c> の revision
    /// （ZMK 公式のテンプレートはこの書き方）。どちらも無ければ main。
    /// </summary>
    public static string ParseRevision(string westYml)
    {
        var lines = westYml.Replace("\r\n", "\n").Split('\n').Select(StripComment).ToList();
        string? defaults = null;

        for (var i = 0; i < lines.Count; i++)
        {
            if (Regex.IsMatch(lines[i], @"^\s*defaults\s*:\s*$"))
            {
                var indent = Indent(lines[i]);
                for (var j = i + 1; j < lines.Count && (lines[j].Trim().Length == 0 || Indent(lines[j]) > indent); j++)
                {
                    if (Revision(lines[j]) is { } value) defaults ??= value;
                }

                continue;
            }

            var project = Regex.Match(lines[i], @"^(\s*)-\s*name\s*:\s*[""']?zmk[""']?\s*$");
            if (!project.Success) continue;

            for (var j = i + 1; j < lines.Count; j++)
            {
                if (Regex.IsMatch(lines[j], @"^\s*-\s")) break;   // 次のプロジェクト
                if (lines[j].Trim().Length > 0 && Indent(lines[j]) <= project.Groups[1].Length) break;   // projects の外

                if (Revision(lines[j]) is { } value) return value;
            }
        }

        return defaults ?? "main";
    }

    private static string? Revision(string line)
    {
        var match = Regex.Match(line, @"^\s*revision\s*:\s*[""']?([^""'\s]+)[""']?\s*$");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static int Indent(string line) => line.Length - line.TrimStart().Length;

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        return hash < 0 ? line : line[..hash];
    }

    private static IEnumerable<string> SplitNames(string value) =>
        value.Split(new[] { ' ', '\t', ',', '[', ']', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries);

    private static string BaseName(string shield)
    {
        var name = shield.Trim().ToLowerInvariant();

        foreach (var suffix in SideSuffixes)
            if (name.EndsWith(suffix, StringComparison.Ordinal))
                return name[..^suffix.Length];

        return name;
    }

    /// <summary>
    /// 見に行くフォルダ名の候補。kyria_rev3 のようにフォルダ名と一致しない版付きの名前もあるので、
    /// 後ろの _xxx を 1 つずつ外したものも試す。
    /// </summary>
    internal static IEnumerable<string> CandidateFolders(IEnumerable<string> shields)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var shield in shields)
        {
            for (var name = BaseName(shield); name.Length > 0;)
            {
                if (seen.Add(name)) yield return name;

                var cut = name.LastIndexOf('_');
                if (cut <= 0) break;
                name = name[..cut];
            }
        }
    }

    // ---- 取得 ----

    /// <summary>
    /// シールドの定義を取ってきて <paramref name="cacheRoot"/> の下に保存する。
    /// 指定の版に無ければ main も試す（古い版には物理レイアウトの定義が無い）。
    /// </summary>
    public static async Task<ZmkShieldResult> DownloadAsync(
        GitHubSource source, IReadOnlyList<string> shields, string revision, string cacheRoot,
        CancellationToken cancellation = default)
    {
        if (shields.Count == 0) throw new GitHubSourceException(Strings.ZmkShieldUnknown);

        var revisions = revision == "main" ? new[] { "main" } : new[] { revision, "main" };
        var calls = 0;

        foreach (var folder in CandidateFolders(shields))
        {
            foreach (var reference in revisions)
            {
                if (calls++ >= MaxApiCalls)
                    throw new GitHubSourceException(Strings.ZmkShieldNotFound(string.Join(", ", shields)));

                IReadOnlyList<string> names;
                try
                {
                    names = await source.ListDirectoryAsync(Zmk, $"{ShieldsPath}/{folder}", reference, cancellation);
                }
                catch (GitHubSourceException ex) when (ex.IsNotFound)
                {
                    continue;
                }

                var sources = names.Where(IsDevicetree).Select(n => $"{ShieldsPath}/{folder}/{n}").ToList();
                if (sources.Count == 0) continue;

                var target = Path.Combine(Path.GetFullPath(cacheRoot), $"{folder}@{SafeName(reference)}");
                var saved = false;

                await GitHubSource.WithGateAsync(async () =>
                {
                    var files = await FetchWithIncludesAsync(source, reference, sources, cancellation);

                    if (!files.Values.Any(b => Encoding.UTF8.GetString(b).Contains(PhysicalLayoutDefinition, StringComparison.Ordinal)))
                        return;

                    await GitHubSource.DownloadIntoAsync(target, files.Keys, path => Task.FromResult(files[path]));
                    saved = true;
                }, cancellation);

                if (saved)
                {
                    var shieldFolder = Path.Combine(new[] { target }.Concat($"{ShieldsPath}/{folder}".Split('/')).ToArray());
                    return new ZmkShieldResult(folder, shieldFolder);
                }
            }
        }

        throw new GitHubSourceException(Strings.ZmkShieldNotFound(string.Join(", ", shields)));
    }

    private static bool IsDevicetree(string name) =>
        name.EndsWith(".dtsi", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".overlay", StringComparison.OrdinalIgnoreCase);

    /// <summary>シールドのファイルと、それが読み込むレイアウトのファイル（リポジトリ内のパス → 本文）。</summary>
    private static async Task<Dictionary<string, byte[]>> FetchWithIncludesAsync(
        GitHubSource source, string reference, IEnumerable<string> start, CancellationToken cancellation)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var queue = new Queue<string>(start);

        while (queue.Count > 0 && files.Count < MaxFiles)
        {
            var path = queue.Dequeue();
            if (files.ContainsKey(path)) continue;

            byte[] bytes;
            try
            {
                bytes = await source.GetRawAsync(Zmk, reference, path, cancellation);
            }
            catch (GitHubSourceException ex) when (ex.IsNotFound && !path.StartsWith(ShieldsPath, StringComparison.Ordinal))
            {
                continue;   // 読み込み先が見つからなくても、ほかのファイルで足りることがある
            }

            files[path] = bytes;
            var text = Encoding.UTF8.GetString(bytes);

            foreach (Match include in LayoutInclude.Matches(text))
                queue.Enqueue("app/dts/" + include.Groups[1].Value);

            foreach (Match include in LocalInclude.Matches(text))
                if (Relative(path, include.Groups[1].Value) is { } relative)
                    queue.Enqueue(relative);
        }

        return files;
    }

    /// <summary>リポジトリ内のパスからの相対。app/ の外へ出るものや .h は取らない。</summary>
    private static string? Relative(string from, string include)
    {
        if (!IsDevicetree(include)) return null;

        var parts = from.Split('/').SkipLast(1).ToList();

        foreach (var part in include.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;

            if (part == "..")
            {
                if (parts.Count == 0) return null;
                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        var path = string.Join('/', parts);
        return path.StartsWith("app/", StringComparison.Ordinal) ? path : null;
    }

    private static string SafeName(string text) => Regex.Replace(text, @"[^A-Za-z0-9._-]", "_");
}

/// <summary>
/// 設定と <see cref="ZmkShieldSource"/> をつなぐ。GitHub から取得したときの自動取得と、
/// 利用者が「ZMK 本体から取得」を押したときの両方が使う。
/// </summary>
public static class ZmkShieldSync
{
    /// <summary>
    /// キーマップを取ってきた直後に呼ぶ。キーマップの近くにキーの並びが無く、build.yaml からシールドが分かれば、
    /// ZMK 本体から取ってくる。失敗しても推定で表示できるので、例外にせず理由を返す。
    /// </summary>
    public static async Task<string?> AutoAsync(
        AppConfig config, string configPath, GitHubSource source, CancellationToken cancellation = default)
    {
        var previousShield = config.Zmk.Shield;

        config.Zmk.ShieldLayoutFolder = null;
        config.Zmk.Shield = null;

        if (!config.Zmk.IsEnabled) return null;

        var keymap = ConfigPaths.Resolve(configPath, config.Zmk.KeymapFile!);
        if (HasPhysicalLayoutNearby(keymap)) return null;

        var info = ZmkShieldSource.ReadBuildInfo(keymap);
        var shields = info.Shields.Count > 0
            ? info.Shields
            : string.IsNullOrWhiteSpace(previousShield) ? Array.Empty<string>() : new[] { previousShield };

        // どのキーボードか分からなければ、推定で表示する（推定したことは読み込みの警告で伝わる）。
        if (shields.Count == 0) return null;

        try
        {
            await FetchIntoAsync(config, configPath, source, shields, info.Revision, cancellation);
            return null;
        }
        catch (GitHubSourceException ex)
        {
            return Strings.ZmkShieldFetchFailed(ex.Message);
        }
    }

    /// <summary>
    /// 利用者が「ZMK 本体から取得」を押した。<paramref name="shield"/> が空なら build.yaml から決める。
    /// 取れなければ <see cref="GitHubSourceException"/>。
    /// </summary>
    public static async Task FetchAsync(
        AppConfig config, string configPath, GitHubSource source, string? shield,
        CancellationToken cancellation = default)
    {
        if (!config.Zmk.IsEnabled) throw new GitHubSourceException(Strings.ZmkShieldUnknown);

        var keymap = ConfigPaths.Resolve(configPath, config.Zmk.KeymapFile!);
        var info = ZmkShieldSource.ReadBuildInfo(keymap);

        var shields = string.IsNullOrWhiteSpace(shield) ? info.Shields : new[] { shield.Trim() };
        if (shields.Count == 0) throw new GitHubSourceException(Strings.ZmkShieldUnknown);

        await FetchIntoAsync(config, configPath, source, shields, info.Revision, cancellation);
    }

    /// <summary>入力欄に最初から入れておくシールド名。</summary>
    public static string? SuggestShield(AppConfig config, string configPath)
    {
        if (!string.IsNullOrWhiteSpace(config.Zmk.Shield)) return config.Zmk.Shield;
        if (!config.Zmk.IsEnabled) return null;

        return ZmkShieldSource.ReadBuildInfo(ConfigPaths.Resolve(configPath, config.Zmk.KeymapFile!))
            .Shields.FirstOrDefault();
    }

    private static async Task FetchIntoAsync(
        AppConfig config, string configPath, GitHubSource source,
        IReadOnlyList<string> shields, string revision, CancellationToken cancellation)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(configPath))!;

        var result = await ZmkShieldSource.DownloadAsync(
            source, shields, revision, Path.Combine(folder, "cache", "zmk"), cancellation);

        config.Zmk.ShieldLayoutFolder = Path.GetRelativePath(folder, result.Folder).Replace('\\', '/');
        config.Zmk.Shield = result.Shield;
    }

    /// <summary>キーマップ自身か、そのフォルダ以下の .dtsi / .overlay に物理レイアウトが定義されているか。</summary>
    internal static bool HasPhysicalLayoutNearby(string keymapPath)
    {
        const string marker = ZmkShieldSource.PhysicalLayoutDefinition;

        try
        {
            if (File.Exists(keymapPath) && File.ReadAllText(keymapPath).Contains(marker, StringComparison.Ordinal))
                return true;

            var folder = Path.GetDirectoryName(Path.GetFullPath(keymapPath));
            if (folder is null || !Directory.Exists(folder)) return false;

            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 };

            return Directory.EnumerateFiles(folder, "*.dtsi", options)
                .Concat(Directory.EnumerateFiles(folder, "*.overlay", options))
                .Take(200)
                .Any(path => File.ReadAllText(path).Contains(marker, StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
