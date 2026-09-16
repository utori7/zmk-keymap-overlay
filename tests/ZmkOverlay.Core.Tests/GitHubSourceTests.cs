using System.Net;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Text;
using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.Core.Tests;

/// <summary>
/// GitHub からの取得。実際には通信せず、偽の応答を返す HttpMessageHandler で流れを確かめる。
/// </summary>
public class GitHubSourceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "zmk-github-" + Guid.NewGuid().ToString("N"));

    // ---- URL の解釈 ----

    [Theory]
    [InlineData("https://github.com/utori7/zmk-config-Pyuron", "utori7", "zmk-config-Pyuron", null, null)]
    [InlineData("github.com/utori7/zmk-config-Pyuron.git", "utori7", "zmk-config-Pyuron", null, null)]
    [InlineData("  utori7/zmk-config-Pyuron  ", "utori7", "zmk-config-Pyuron", null, null)]
    [InlineData("https://github.com/o/r/tree/paw3220/config", "o", "r", "paw3220", "config")]
    [InlineData("https://github.com/o/r/blob/main/config/Pyuron.keymap?plain=1#L10", "o", "r", "main", "config/Pyuron.keymap")]
    [InlineData("https://www.github.com/o/r/", "o", "r", null, null)]
    public void UrlsCopiedFromTheBrowserAreUnderstood(
        string text, string owner, string repository, string? branch, string? path)
    {
        Assert.True(GitHubLocation.TryParse(text, out var location));
        Assert.Equal(new GitHubLocation(owner, repository, branch, path), location);
    }

    [Theory]
    [InlineData("")]
    [InlineData("utori7")]
    [InlineData("https://gitlab.com/o/r")]
    [InlineData("github.com/o/..")]
    [InlineData("o/r with space")]
    public void OtherTextIsRejected(string text)
    {
        Assert.False(GitHubLocation.TryParse(text, out _));
    }

    // ---- ファイルの選び方 ----

    private static readonly GitHubTree PyuronTree = new("paw3220", new[]
    {
        new GitHubFile("README.md", 10),
        new GitHubFile("build.yaml", 10),
        new GitHubFile("config/Pyuron.keymap", 100),
        new GitHubFile("config/west.yml", 10),
        new GitHubFile("config/boards/shields/Pyuron/Pyuron.dtsi", 100),
        new GitHubFile("config/boards/shields/Pyuron/Pyuron_R.overlay", 100),
        new GitHubFile("config/boards/shields/Pyuron/Pyuron.zmk.yml", 10),
        new GitHubFile("other/big.dtsi", 5_000_000),
        new GitHubFile("zmk/samples/old.keymap", 100),
    });

    [Fact]
    public void ShallowKeymapsComeFirst()
    {
        Assert.Equal(new[] { "config/Pyuron.keymap", "zmk/samples/old.keymap" }, PyuronTree.KeymapPaths);
    }

    [Fact]
    public void OnlyDevicetreeFilesUnderTheKeymapFolderAreDownloaded()
    {
        var paths = GitHubSource.FilesToDownload(PyuronTree, "config/Pyuron.keymap").Select(f => f.Path);

        Assert.Equal(new[]
        {
            "config/Pyuron.keymap",
            "config/boards/shields/Pyuron/Pyuron.dtsi",
            "config/boards/shields/Pyuron/Pyuron_R.overlay",
        }, paths);
    }

    // ---- 取得の流れ ----

    [Fact]
    public async Task RepositoryIsFetchedIntoTheCacheFolder()
    {
        var github = new FakeGitHub()
            .Json("https://api.github.com/repos/o/r", """{ "default_branch": "paw3220" }""")
            .Json("https://api.github.com/repos/o/r/git/trees/paw3220?recursive=1", """
                { "tree": [
                    { "path": "config", "type": "tree" },
                    { "path": "config/Pyuron.keymap", "type": "blob", "size": 12 },
                    { "path": "config/boards/shields/Pyuron/Pyuron.dtsi", "type": "blob", "size": 12 },
                    { "path": "README.md", "type": "blob", "size": 12 }
                ] }
                """)
            .Text("https://raw.githubusercontent.com/o/r/paw3220/config/Pyuron.keymap", "keymap body")
            .Text("https://raw.githubusercontent.com/o/r/paw3220/config/boards/shields/Pyuron/Pyuron.dtsi", "dtsi body");

        var source = new GitHubSource(new HttpClient(github));
        GitHubLocation.TryParse("https://github.com/o/r", out var location);

        var tree = await source.ListAsync(location);
        Assert.Equal("paw3220", tree.Branch);   // ブランチを指定しなければ既定のブランチ

        var cache = Path.Combine(_directory, "cache", "github", "o", "r");
        var keymap = await source.DownloadAsync(location, tree, tree.KeymapPaths[0], cache);

        Assert.Equal(Path.Combine(cache, "config", "Pyuron.keymap"), keymap);
        Assert.Equal("keymap body", File.ReadAllText(keymap));
        Assert.True(File.Exists(Path.Combine(cache, "config", "boards", "shields", "Pyuron", "Pyuron.dtsi")));
        Assert.False(File.Exists(Path.Combine(cache, "README.md")));
        Assert.All(github.UserAgents, agent => Assert.Equal("ZmkOverlay", agent));
    }

    [Fact]
    public async Task FailedDownloadKeepsThePreviousCache()
    {
        // 前回取ってきたものがある状態で、今回は本文の取得に失敗する。
        var cache = Path.Combine(_directory, "cache", "github", "o", "r");
        Directory.CreateDirectory(Path.Combine(cache, "config"));
        File.WriteAllText(Path.Combine(cache, "config", "Pyuron.keymap"), "previous");

        var github = new FakeGitHub();   // 何を聞いても 404
        var source = new GitHubSource(new HttpClient(github));
        var tree = new GitHubTree("main", new[] { new GitHubFile("config/Pyuron.keymap", 10) });

        await Assert.ThrowsAsync<GitHubSourceException>(
            () => source.DownloadAsync(new GitHubLocation("o", "r"), tree, "config/Pyuron.keymap", cache));

        Assert.Equal("previous", File.ReadAllText(Path.Combine(cache, "config", "Pyuron.keymap")));
        Assert.False(Directory.Exists(cache + ".downloading"));
    }

    [Fact]
    public async Task MissingOrPrivateRepositoryIsExplained()
    {
        var source = new GitHubSource(new HttpClient(new FakeGitHub()));

        var error = await Assert.ThrowsAsync<GitHubSourceException>(
            () => source.ListAsync(new GitHubLocation("o", "secret")));

        Assert.Equal(Strings.GitHubNotFound("o/secret"), error.Message);
    }

    [Fact]
    public async Task UsingARepositoryPointsTheConfigAtTheSavedKeymap()
    {
        var github = new FakeGitHub()
            .Json("https://api.github.com/repos/o/r/git/trees/main?recursive=1",
                """{ "tree": [ { "path": "config/corne.keymap", "type": "blob", "size": 5 } ] }""")
            .Text("https://raw.githubusercontent.com/o/r/main/config/corne.keymap", "body");

        var configPath = Path.Combine(_directory, "config.json");
        var config = new AppConfig();
        var source = new GitHubSource(new HttpClient(github));
        var location = new GitHubLocation("o", "r", "main");

        var tree = await source.ListAsync(location);
        await GitHubSync.UseAsync(config, configPath, source, location, tree, "config/corne.keymap");

        Assert.True(config.Zmk.IsGitHub);
        Assert.Equal("o/r", config.Zmk.GitHub.Repository);
        Assert.Equal("config/corne.keymap", config.Zmk.GitHub.KeymapPath);

        // 設定ファイルからの相対パス。フォルダごと持ち運んでも読める。
        Assert.Equal("cache/github/o/r/config/corne.keymap", config.Zmk.KeymapFile);
        Assert.Equal("body", File.ReadAllText(ConfigPaths.Resolve(configPath, config.Zmk.KeymapFile!)));
    }

    [Fact]
    public async Task RefreshKeepsTheLayoutFileTheUserChose()
    {
        var github = new FakeGitHub()
            .Json("https://api.github.com/repos/o/r/git/trees/main?recursive=1",
                """{ "tree": [ { "path": "config/corne.keymap", "type": "blob", "size": 5 } ] }""")
            .Text("https://raw.githubusercontent.com/o/r/main/config/corne.keymap", "new body");

        var configPath = Path.Combine(_directory, "config.json");
        var config = new AppConfig();
        config.Zmk.Source = "github";
        config.Zmk.GitHub = new GitHubSourceConfig { Repository = "o/r", Branch = "main", KeymapPath = "config/gone.keymap" };
        config.Zmk.PhysicalLayoutFile = "C:/my/layout.dtsi";

        await GitHubSync.RefreshAsync(config, configPath, new GitHubSource(new HttpClient(github)));

        Assert.Equal("config/corne.keymap", config.Zmk.GitHub.KeymapPath);   // 無くなっていたので見つかったものへ
        Assert.Equal("C:/my/layout.dtsi", config.Zmk.PhysicalLayoutFile);
    }

    [Fact]
    public async Task DefaultBranchIsRememberedForTheEditPage()
    {
        var github = new FakeGitHub()
            .Json("https://api.github.com/repos/o/r", """{ "default_branch": "paw3220" }""")
            .Json("https://api.github.com/repos/o/r/git/trees/paw3220?recursive=1",
                """{ "tree": [ { "path": "config/Pyuron.keymap", "type": "blob", "size": 5 } ] }""")
            .Text("https://raw.githubusercontent.com/o/r/paw3220/config/Pyuron.keymap", "body");

        var config = new AppConfig();
        var source = new GitHubSource(new HttpClient(github));
        var location = new GitHubLocation("o", "r");   // ブランチを指定していない

        var tree = await source.ListAsync(location);
        await GitHubSync.UseAsync(config, Path.Combine(_directory, "config.json"), source, location, tree, "config/Pyuron.keymap");

        Assert.Equal("paw3220", config.Zmk.GitHub.Branch);
        Assert.Equal("https://github.com/o/r/edit/paw3220/config/Pyuron.keymap", GitHubSync.EditUrl(config.Zmk.GitHub));
        Assert.Equal("https://github.com/o/r/actions", GitHubSync.ActionsUrl(config.Zmk.GitHub));
    }

    /// <summary>決まった URL にだけ応答を返し、それ以外は 404 にする。</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _bodies = new();

        public List<string> UserAgents { get; } = new();

        public FakeGitHub Json(string url, string body) => Text(url, body);

        public FakeGitHub Text(string url, string body)
        {
            _bodies[url] = body;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UserAgents.Add(request.Headers.UserAgent.ToString());

            var response = _bodies.TryGetValue(request.RequestUri!.AbsoluteUri, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);

            return Task.FromResult(response);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 後始末に失敗してもテスト結果には関係ない。
        }
    }
}
