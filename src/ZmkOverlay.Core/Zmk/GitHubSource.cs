using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Text;

namespace ZmkOverlay.Core.Zmk;

/// <summary>GitHub 上の zmk-config を指す場所。利用者が貼り付けた URL から作る。</summary>
public sealed record GitHubLocation(string Owner, string Repository, string? Branch = null, string? Path = null)
{
    private static readonly Regex NamePart = new(@"^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    public string FullName => $"{Owner}/{Repository}";

    /// <summary>
    /// ブラウザのアドレス欄からコピーしてくる形をひととおり受け付ける。
    ///
    ///   https://github.com/owner/repo
    ///   github.com/owner/repo.git
    ///   owner/repo
    ///   https://github.com/owner/repo/tree/branch/config
    ///   https://github.com/owner/repo/blob/branch/config/x.keymap
    ///
    /// ブランチ名に / を含むものは URL からは区別できないので、最初の区切りまでをブランチとみなす。
    /// </summary>
    public static bool TryParse(string? text, out GitHubLocation location)
    {
        location = new GitHubLocation("", "");
        if (string.IsNullOrWhiteSpace(text)) return false;

        var value = text.Trim();

        var cut = value.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) value = value[..cut];

        value = Regex.Replace(value, @"^https?://", "", RegexOptions.IgnoreCase);

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        if (parts[0].Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || parts[0].Equals("www.github.com", StringComparison.OrdinalIgnoreCase))
        {
            parts = parts[1..];
        }
        else if (parts[0].Contains('.'))
        {
            // GitHub のユーザー名に . は入らない。gitlab.com/... のような別のサイト。
            return false;
        }

        if (parts.Length < 2) return false;

        var owner = parts[0];
        var repository = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];

        if (!IsName(owner) || !IsName(repository)) return false;

        string? branch = null;
        string? path = null;

        if (parts.Length >= 4 && parts[2] is "tree" or "blob")
        {
            branch = Uri.UnescapeDataString(parts[3]);
            if (parts.Length > 4) path = string.Join('/', parts[4..].Select(Uri.UnescapeDataString));
        }

        location = new GitHubLocation(owner, repository, branch, path);
        return true;
    }

    /// <summary>名前として使える文字だけか。"." や ".." はフォルダを抜け出せるので断る。</summary>
    private static bool IsName(string name) => name is not ("." or "..") && NamePart.IsMatch(name);
}

/// <summary>リポジトリ内のファイル 1 個。パスは / 区切り。</summary>
public sealed record GitHubFile(string Path, long Size);

/// <summary>あるブランチのファイル一覧。</summary>
public sealed record GitHubTree(string Branch, IReadOnlyList<GitHubFile> Files)
{
    /// <summary>キーマップの候補。浅い場所にあるものほど先（ふつうは config/ 直下）。</summary>
    public IReadOnlyList<string> KeymapPaths =>
        Files
            .Where(f => f.Path.EndsWith(".keymap", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Path)
            .OrderBy(p => p.Count(c => c == '/'))
            .ThenBy(p => p, StringComparer.Ordinal)
            .ToList();
}

/// <summary>
/// GitHub から取ってきて、設定をその保存分を読む形に書き換える。設定画面とトレイの再読み込みが共有する。
/// 保存と反映は呼び出し側（アプリの反映経路）に任せる。
/// </summary>
public static class GitHubSync
{
    /// <summary>リポジトリのキーマップを取ってきて、設定をそれを読む形にする。</summary>
    public static async Task UseAsync(
        AppConfig config, string configPath, GitHubSource source,
        GitHubLocation location, GitHubTree tree, string keymapPath,
        CancellationToken cancellation = default)
    {
        var local = await source.DownloadAsync(
            location, tree, keymapPath, GitHubSource.CacheFolder(configPath, location), cancellation);

        var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(configPath))!;

        config.Zmk.Source = "github";
        config.Zmk.GitHub = new GitHubSourceConfig
        {
            Repository = location.FullName,

            // 既定のブランチから取ったときも、実際の名前を持つ。
            // GitHub の編集画面の URL にはブランチ名が要る。
            Branch = location.Branch ?? tree.Branch,
            KeymapPath = keymapPath,
        };

        // 設定ファイルからの相対で持つ。フォルダごと別の PC に持っていっても読める。
        config.Zmk.KeymapFile = System.IO.Path.GetRelativePath(folder, local).Replace('\\', '/');
        config.Zmk.PhysicalLayoutFile = null;
    }

    /// <summary>
    /// キーマップを GitHub のブラウザ画面で編集する URL。利用者はここに書き換え済みの内容を貼り付ける。
    /// </summary>
    public static string EditUrl(GitHubSourceConfig github) =>
        $"https://github.com/{github.Repository}/edit/" +
        $"{GitHubSource.EscapePath(github.Branch ?? "HEAD")}/{GitHubSource.EscapePath(github.KeymapPath ?? "")}";

    /// <summary>ビルドの進み具合（GitHub Actions）の一覧。できたファームもここから取る。</summary>
    public static string ActionsUrl(GitHubSourceConfig github) =>
        $"https://github.com/{github.Repository}/actions";

    /// <summary>
    /// 設定に書かれたリポジトリから取り直す。利用者が物理レイアウトを選んでいたら、それは残す。
    /// 前回のキーマップがリポジトリから無くなっていたら、見つかった最初のものを使う。
    /// </summary>
    public static async Task RefreshAsync(
        AppConfig config, string configPath, GitHubSource source, CancellationToken cancellation = default)
    {
        var github = config.Zmk.GitHub;

        if (!GitHubLocation.TryParse(github.Repository, out var parsed))
            throw new GitHubSourceException(Strings.GitHubNotFound(github.Repository));

        var location = parsed with { Branch = string.IsNullOrWhiteSpace(github.Branch) ? null : github.Branch };
        var tree = await source.ListAsync(location, cancellation);

        var keymap = github.KeymapPath is { } previous && tree.KeymapPaths.Contains(previous)
            ? previous
            : tree.KeymapPaths.FirstOrDefault()
              ?? throw new GitHubSourceException(Strings.GitHubNoKeymap(location.FullName));

        var chosenLayout = config.Zmk.PhysicalLayoutFile;

        await UseAsync(config, configPath, source, location, tree, keymap, cancellation);

        config.Zmk.PhysicalLayoutFile = chosenLayout;
    }
}

public sealed class GitHubSourceException : Exception
{
    public GitHubSourceException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// GitHub の公開リポジトリから zmk-config のファイルを取ってきて、この PC に保存する。
///
/// 保存したあとはローカルのファイルとして、今までと同じ読み込みに渡す。物理レイアウトも
/// 「キーマップのフォルダ以下から探す」がそのまま効く。
///
/// 通信するのは利用者が取得・取り直しを選んだときだけで、起動時には通信しない。
/// 会社 PC で外部への通信が遮られていても、保存済みのもので動き続けるように。
///
/// 認証はしないので、公開リポジトリだけが対象。GitHub API は認証なしだと 1 時間に 60 回まで呼べる。
/// 1 回の取得で API は多くても 2 回（リポジトリ情報とファイル一覧）で、本文は raw.githubusercontent.com から取る。
/// </summary>
public sealed class GitHubSource
{
    private const string ApiBase = "https://api.github.com";
    private const string RawBase = "https://raw.githubusercontent.com";

    /// <summary>キーマップと、そこから include されうるものだけを取る。</summary>
    private static readonly string[] SourceExtensions = { ".keymap", ".dtsi", ".overlay", ".dts", ".h" };

    private const int MaxFiles = 300;
    private const long MaxFileBytes = 1024 * 1024;

    private readonly HttpClient _http;

    /// <param name="http">テストで偽の応答を返すときだけ渡す。省略時はシステムのプロキシ設定に従う。</param>
    public GitHubSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>取ってきたファイルの置き場所。設定ファイルの隣に置き、フォルダごと持ち運べるようにする。</summary>
    public static string CacheFolder(string configPath, GitHubLocation location) =>
        System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(configPath))!,
            "cache", "github", location.Owner, location.Repository);

    /// <summary>ブランチ（指定が無ければ既定のブランチ）と、そこにあるファイルの一覧を得る。</summary>
    public async Task<GitHubTree> ListAsync(GitHubLocation location, CancellationToken cancellation = default)
    {
        var repository = $"{ApiBase}/repos/{Uri.EscapeDataString(location.Owner)}/{Uri.EscapeDataString(location.Repository)}";

        var branch = location.Branch;
        if (string.IsNullOrWhiteSpace(branch))
        {
            using var info = await GetJsonAsync(repository, location, cancellation);
            branch = info.RootElement.TryGetProperty("default_branch", out var value) ? value.GetString() : null;
            branch ??= "main";
        }

        using var tree = await GetJsonAsync(
            $"{repository}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1", location, cancellation);

        var files = new List<GitHubFile>();

        if (tree.RootElement.TryGetProperty("tree", out var entries))
        {
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.GetProperty("type").GetString() != "blob") continue;
                if (entry.GetProperty("path").GetString() is not { } path) continue;

                var size = entry.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                files.Add(new GitHubFile(path, size));
            }
        }

        return new GitHubTree(branch, files);
    }

    /// <summary>
    /// 取ってくるファイル。キーマップのあるフォルダ以下の、devicetree 関連のファイルだけ。
    /// zmk-config ではシールドの定義（物理レイアウト）も config/ 以下にまとまっているので、
    /// リポジトリ全体を取る必要はない。
    /// </summary>
    public static IReadOnlyList<GitHubFile> FilesToDownload(GitHubTree tree, string keymapPath)
    {
        var slash = keymapPath.LastIndexOf('/');
        var folder = slash < 0 ? "" : keymapPath[..(slash + 1)];

        return tree.Files
            .Where(f => f.Path.StartsWith(folder, StringComparison.Ordinal))
            .Where(f => SourceExtensions.Any(ext => f.Path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => f.Size <= MaxFileBytes)
            .OrderBy(f => f.Path == keymapPath ? 0 : 1)   // 上限で切るときもキーマップだけは必ず残す
            .Take(MaxFiles)
            .ToList();
    }

    /// <summary>
    /// ファイルを取ってきて <paramref name="cacheFolder"/> に保存し、キーマップの保存先を返す。
    ///
    /// いったん別のフォルダに全部取ってから入れ替える。途中で通信が切れても、前回の保存分は壊れない。
    /// </summary>
    public async Task<string> DownloadAsync(
        GitHubLocation location, GitHubTree tree, string keymapPath, string cacheFolder,
        CancellationToken cancellation = default)
    {
        var target = System.IO.Path.GetFullPath(cacheFolder).TrimEnd('\\', '/');
        var staging = target + ".downloading";

        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        try
        {
            foreach (var file in FilesToDownload(tree, keymapPath))
            {
                var local = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(staging, file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)));

                // リポジトリ内のパスが保存先の外を指していたら書かない。
                if (!local.StartsWith(staging + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;

                var url = $"{RawBase}/{Uri.EscapeDataString(location.Owner)}/{Uri.EscapeDataString(location.Repository)}/" +
                          $"{EscapePath(tree.Branch)}/{EscapePath(file.Path)}";

                var bytes = await GetBytesAsync(url, location, cancellation);

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(local)!);
                await File.WriteAllBytesAsync(local, bytes, cancellation);
            }

            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(staging, target);
        }
        catch
        {
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
            catch (IOException)
            {
                // 後始末に失敗しても、元の失敗のほうを伝える。
            }

            throw;
        }

        return System.IO.Path.Combine(target, keymapPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }

    internal static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private async Task<JsonDocument> GetJsonAsync(string url, GitHubLocation location, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        return JsonDocument.Parse(await SendAsync(request, location, cancellation));
    }

    private async Task<byte[]> GetBytesAsync(string url, GitHubLocation location, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await SendAsync(request, location, cancellation);
    }

    private async Task<byte[]> SendAsync(HttpRequestMessage request, GitHubLocation location, CancellationToken cancellation)
    {
        // GitHub は User-Agent の無い要求を断る。
        request.Headers.UserAgent.ParseAdd("ZmkOverlay");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellation);
        }
        catch (HttpRequestException ex)
        {
            throw new GitHubSourceException(Strings.GitHubUnreachable(ex.Message), ex);
        }
        catch (TaskCanceledException ex) when (!cancellation.IsCancellationRequested)
        {
            // 利用者が取り消したのではなく、時間切れ。
            throw new GitHubSourceException(Strings.GitHubUnreachable(ex.Message), ex);
        }

        using (response)
        {
            // 非公開のリポジトリも、認証なしでは 404 に見える。
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new GitHubSourceException(Strings.GitHubNotFound(location.FullName));

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new GitHubSourceException(Strings.GitHubRateLimited);

            if (!response.IsSuccessStatusCode)
                throw new GitHubSourceException(Strings.GitHubFailed((int)response.StatusCode));

            return await response.Content.ReadAsByteArrayAsync(cancellation);
        }
    }
}
