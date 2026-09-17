using System.Net;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;
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
    [InlineData("https://github.com/someone/zmk-config", "someone", "zmk-config", null, null)]
    [InlineData("github.com/someone/zmk-config.git", "someone", "zmk-config", null, null)]
    [InlineData("  someone/zmk-config  ", "someone", "zmk-config", null, null)]
    [InlineData("https://github.com/o/r/tree/dev/config", "o", "r", "dev", "config")]
    [InlineData("https://github.com/o/r/blob/main/config/demo40.keymap?plain=1#L10", "o", "r", "main", "config/demo40.keymap")]
    [InlineData("https://www.github.com/o/r/", "o", "r", null, null)]
    public void UrlsCopiedFromTheBrowserAreUnderstood(
        string text, string owner, string repository, string? branch, string? path)
    {
        Assert.True(GitHubLocation.TryParse(text, out var location));
        Assert.Equal(new GitHubLocation(owner, repository, branch, path), location);
    }

    [Theory]
    [InlineData("")]
    [InlineData("someone")]
    [InlineData("https://gitlab.com/o/r")]
    [InlineData("github.com/o/..")]
    [InlineData("o/r with space")]
    public void OtherTextIsRejected(string text)
    {
        Assert.False(GitHubLocation.TryParse(text, out _));
    }

    // ---- ファイルの選び方 ----

    private static readonly GitHubTree DemoTree = new("dev", new[]
    {
        new GitHubFile("README.md", 10),
        new GitHubFile("build.yaml", 10),
        new GitHubFile("config/demo40.keymap", 100),
        new GitHubFile("config/west.yml", 10),
        new GitHubFile("config/boards/shields/demo40/demo40.dtsi", 100),
        new GitHubFile("config/boards/shields/demo40/demo40_right.overlay", 100),
        new GitHubFile("config/boards/shields/demo40/demo40.zmk.yml", 10),
        new GitHubFile("other/big.dtsi", 5_000_000),
        new GitHubFile("other/west.yml", 10),
        new GitHubFile("zmk/samples/old.keymap", 100),
    });

    [Fact]
    public void ShallowKeymapsComeFirst()
    {
        Assert.Equal(new[] { "config/demo40.keymap", "zmk/samples/old.keymap" }, DemoTree.KeymapPaths);
    }

    [Fact]
    public void DevicetreeFilesUnderTheKeymapFolderAndTheBuildDescriptionAreDownloaded()
    {
        var paths = GitHubSource.FilesToDownload(DemoTree, "config/demo40.keymap").Select(f => f.Path);

        // build.yaml と west.yml は、キーの並びを ZMK 本体から取りに行く先を決めるのに使う。
        Assert.Equal(new[]
        {
            "config/demo40.keymap",
            "build.yaml",
            "config/west.yml",
            "config/boards/shields/demo40/demo40.dtsi",
            "config/boards/shields/demo40/demo40_right.overlay",
        }, paths);
    }

    // ---- 取得の流れ ----

    [Fact]
    public async Task RepositoryIsFetchedIntoTheCacheFolder()
    {
        var github = new FakeGitHub()
            .Json("https://api.github.com/repos/o/r", """{ "default_branch": "dev" }""")
            .Json("https://api.github.com/repos/o/r/git/trees/dev?recursive=1", """
                { "tree": [
                    { "path": "config", "type": "tree" },
                    { "path": "config/demo40.keymap", "type": "blob", "size": 12 },
                    { "path": "config/boards/shields/demo40/demo40.dtsi", "type": "blob", "size": 12 },
                    { "path": "README.md", "type": "blob", "size": 12 }
                ] }
                """)
            .Text("https://raw.githubusercontent.com/o/r/dev/config/demo40.keymap", "keymap body")
            .Text("https://raw.githubusercontent.com/o/r/dev/config/boards/shields/demo40/demo40.dtsi", "dtsi body");

        var source = new GitHubSource(new HttpClient(github));
        GitHubLocation.TryParse("https://github.com/o/r", out var location);

        var tree = await source.ListAsync(location);
        Assert.Equal("dev", tree.Branch);   // ブランチを指定しなければ既定のブランチ

        var cache = Path.Combine(_directory, "cache", "github", "o", "r");
        var keymap = await source.DownloadAsync(location, tree, tree.KeymapPaths[0], cache);

        Assert.Equal(Path.Combine(cache, "config", "demo40.keymap"), keymap);
        Assert.Equal("keymap body", File.ReadAllText(keymap));
        Assert.True(File.Exists(Path.Combine(cache, "config", "boards", "shields", "demo40", "demo40.dtsi")));
        Assert.False(File.Exists(Path.Combine(cache, "README.md")));
        Assert.All(github.UserAgents, agent => Assert.Equal("ZmkOverlay", agent));
    }

    [Fact]
    public async Task FailedDownloadKeepsThePreviousCache()
    {
        // 前回取ってきたものがある状態で、今回は本文の取得に失敗する。
        var cache = Path.Combine(_directory, "cache", "github", "o", "r");
        Directory.CreateDirectory(Path.Combine(cache, "config"));
        File.WriteAllText(Path.Combine(cache, "config", "demo40.keymap"), "previous");

        var github = new FakeGitHub();   // 何を聞いても 404
        var source = new GitHubSource(new HttpClient(github));
        var tree = new GitHubTree("main", new[] { new GitHubFile("config/demo40.keymap", 10) });

        await Assert.ThrowsAsync<GitHubSourceException>(
            () => source.DownloadAsync(new GitHubLocation("o", "r"), tree, "config/demo40.keymap", cache));

        Assert.Equal("previous", File.ReadAllText(Path.Combine(cache, "config", "demo40.keymap")));
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
            .Json("https://api.github.com/repos/o/r", """{ "default_branch": "dev" }""")
            .Json("https://api.github.com/repos/o/r/git/trees/dev?recursive=1",
                """{ "tree": [ { "path": "config/demo40.keymap", "type": "blob", "size": 5 } ] }""")
            .Text("https://raw.githubusercontent.com/o/r/dev/config/demo40.keymap", "body");

        var config = new AppConfig();
        var source = new GitHubSource(new HttpClient(github));
        var location = new GitHubLocation("o", "r");   // ブランチを指定していない

        var tree = await source.ListAsync(location);
        await GitHubSync.UseAsync(config, Path.Combine(_directory, "config.json"), source, location, tree, "config/demo40.keymap");

        Assert.Equal("dev", config.Zmk.GitHub.Branch);
        Assert.Equal("https://github.com/o/r/edit/dev/config/demo40.keymap", GitHubSync.EditUrl(config.Zmk.GitHub));
        Assert.Equal("https://github.com/o/r/actions", GitHubSync.ActionsUrl(config.Zmk.GitHub));
    }

    // ---- 応答が壊れているとき ----

    [Fact]
    public async Task HtmlFromAProxyIsReportedAsAnUnexpectedResponse()
    {
        var github = new FakeGitHub().Text("https://api.github.com/repos/o/r", "<html>blocked</html>");
        var source = new GitHubSource(new HttpClient(github));

        var error = await Assert.ThrowsAsync<GitHubSourceException>(() => source.ListAsync(new GitHubLocation("o", "r")));

        Assert.Equal(Strings.GitHubUnexpectedResponse, error.Message);
    }

    [Fact]
    public async Task ForbiddenIsARateLimitOnlyWhenGitHubSaysSo()
    {
        var github = new FakeGitHub()
            .Status("https://api.github.com/repos/o/limited", HttpStatusCode.Forbidden, ("X-RateLimit-Remaining", "0"))
            .Status("https://api.github.com/repos/o/blocked", HttpStatusCode.Forbidden);
        var source = new GitHubSource(new HttpClient(github));

        var limited = await Assert.ThrowsAsync<GitHubSourceException>(() => source.ListAsync(new GitHubLocation("o", "limited")));
        var blocked = await Assert.ThrowsAsync<GitHubSourceException>(() => source.ListAsync(new GitHubLocation("o", "blocked")));

        Assert.Equal(Strings.GitHubRateLimited, limited.Message);
        Assert.Equal(Strings.GitHubFailed(403), blocked.Message);
    }

    [Fact]
    public async Task UnexpectedJsonShapeIsNotACrash()
    {
        var github = new FakeGitHub()
            .Json("https://api.github.com/repos/o/r/git/trees/main?recursive=1",
                """{ "tree": [ { "path": 5 }, { "type": "blob" }, "text", { "path": "config/a.keymap", "type": "blob" } ] }""");

        var tree = await new GitHubSource(new HttpClient(github)).ListAsync(new GitHubLocation("o", "r", "main"));

        Assert.Equal(new[] { "config/a.keymap" }, tree.KeymapPaths);
    }

    // ---- ZMK 本体からキーの並びを取る ----

    [Theory]
    [InlineData("include:\n  - board: nice_nano_v2\n    shield: corne_left nice_view_adapter nice_view\n  - board: nice_nano_v2\n    shield: corne_right nice_view_adapter nice_view\n",
                "corne,nice_view_adapter,nice_view")]
    [InlineData("board: [ \"nice_nano_v2\" ]\nshield: [ \"lily58_left\", \"lily58_right\" ]\n", "lily58")]
    [InlineData("include:\n  - board: xiao_ble\n    shield:\n      - settings_reset\n      - kyria_rev3_left # the left half\n", "kyria_rev3,settings_reset")]
    [InlineData("# shield: nothing here\ninclude: []\n", "")]
    public void ShieldNamesAreReadFromBuildYaml(string yaml, string expected)
    {
        Assert.Equal(expected, string.Join(",", ZmkShieldSource.ParseShields(yaml)));
    }

    [Fact]
    public void ZmkRevisionIsReadFromWestYml()
    {
        const string west = """
            manifest:
              remotes:
                - name: zmkfirmware
                  url-base: https://github.com/zmkfirmware
              projects:
                - name: zmk-helpers
                  remote: urob
                  revision: v1
                - name: zmk
                  remote: zmkfirmware
                  revision: "v0.3"   # pinned
                  import: app/west.yml
              self:
                path: config
            """;

        Assert.Equal("v0.3", ZmkShieldSource.ParseRevision(west));
        Assert.Equal("main", ZmkShieldSource.ParseRevision("manifest:\n  projects: []\n"));
    }

    [Fact]
    public void ZmkRevisionFallsBackToTheManifestDefault()
    {
        // ZMK 公式の zmk-config テンプレートの書き方。
        const string west = """
            manifest:
              defaults:
                revision: v0.3
              remotes:
                - name: zmkfirmware
                  url-base: https://github.com/zmkfirmware
              projects:
                - name: zmk
                  remote: zmkfirmware
                  import: app/west.yml
              self:
                path: config
            """;

        Assert.Equal("v0.3", ZmkShieldSource.ParseRevision(west));
    }

    [Fact]
    public void VersionedShieldNamesAlsoTryTheFamilyFolder()
    {
        Assert.Equal(
            new[] { "kyria_rev3", "kyria", "corne" },
            ZmkShieldSource.CandidateFolders(new[] { "kyria_rev3_left", "corne_right" }));
    }

    /// <summary>ZMK 本体の Corne を、fixtures/zmk の中身で返す。</summary>
    private static FakeGitHub ZmkCorne(FakeGitHub github, string reference = "main")
    {
        github.Json(
            $"https://api.github.com/repos/zmkfirmware/zmk/contents/app/boards/shields/corne?ref={reference}",
            """
            [
              { "name": "corne.dtsi", "type": "file" },
              { "name": "corne.keymap", "type": "file" },
              { "name": "corne_left.overlay", "type": "file" },
              { "name": "corne_right.overlay", "type": "file" },
              { "name": "boards", "type": "dir" }
            ]
            """);

        foreach (var path in new[]
                 {
                     "app/boards/shields/corne/corne.dtsi",
                     "app/boards/shields/corne/corne_left.overlay",
                     "app/boards/shields/corne/corne_right.overlay",
                     "app/dts/layouts/foostan/corne/5column.dtsi",
                     "app/dts/layouts/foostan/corne/6column.dtsi",
                     "app/dts/layouts/foostan/corne/position_map.dtsi",
                 })
        {
            github.Text($"https://raw.githubusercontent.com/zmkfirmware/zmk/{reference}/{path}", File.ReadAllText(Fixtures.Zmk(path)));
        }

        return github;
    }

    [Fact]
    public async Task ShieldIsFetchedWithTheLayoutsItIncludes()
    {
        var github = ZmkCorne(new FakeGitHub());
        var source = new GitHubSource(new HttpClient(github));
        var cache = Path.Combine(_directory, "cache", "zmk");

        // 指定の版に無ければ main を試す。
        var result = await ZmkShieldSource.DownloadAsync(source, new[] { "corne", "nice_view" }, "v0.3", cache);

        Assert.Equal("corne", result.Shield);
        Assert.True(File.Exists(Path.Combine(result.Folder, "corne.dtsi")));
        Assert.False(File.Exists(Path.Combine(result.Folder, "corne.keymap")));   // キーの並びに要るものだけ
        Assert.True(File.Exists(Path.Combine(cache, "corne@main", "app", "dts", "layouts", "foostan", "corne", "6column.dtsi")));

        var keymap = Path.Combine(_directory, "corne.keymap");
        File.Copy(Fixtures.CorneKeymap, keymap);

        var read = ZmkKeymapReader.Read(new ZmkReadOptions { KeymapPath = keymap, ExtraLayoutFolders = new[] { result.Folder } });
        Assert.Equal(LayoutSource.ZmkRepository, read.LayoutSource);
        Assert.Equal(42, read.Layout.Keys.Count);
    }

    [Fact]
    public async Task UnknownShieldIsExplained()
    {
        var source = new GitHubSource(new HttpClient(new FakeGitHub()));

        var error = await Assert.ThrowsAsync<GitHubSourceException>(
            () => ZmkShieldSource.DownloadAsync(source, new[] { "my_own_board" }, "main", _directory));

        Assert.Equal(Strings.ZmkShieldNotFound("my_own_board"), error.Message);
    }

    /// <summary>標準の Corne の zmk-config。キーマップとビルドの指定だけで、シールドの定義は持たない。</summary>
    private static FakeGitHub CorneConfig(FakeGitHub github) =>
        github
            .Json("https://api.github.com/repos/o/corne-config/git/trees/main?recursive=1", """
                { "tree": [
                    { "path": "build.yaml", "type": "blob", "size": 5 },
                    { "path": "config/corne.keymap", "type": "blob", "size": 5 },
                    { "path": "config/west.yml", "type": "blob", "size": 5 }
                ] }
                """)
            .Text("https://raw.githubusercontent.com/o/corne-config/main/build.yaml",
                "include:\n  - board: nice_nano_v2\n    shield: corne_left\n  - board: nice_nano_v2\n    shield: corne_right\n")
            .Text("https://raw.githubusercontent.com/o/corne-config/main/config/west.yml",
                "manifest:\n  projects:\n    - name: zmk\n      revision: main\n")
            .Text("https://raw.githubusercontent.com/o/corne-config/main/config/corne.keymap",
                File.ReadAllText(Fixtures.CorneKeymap));

    [Fact]
    public async Task UsingAStockKeyboardFetchesItsLayoutFromZmk()
    {
        var github = ZmkCorne(CorneConfig(new FakeGitHub()));
        var source = new GitHubSource(new HttpClient(github));
        var configPath = Path.Combine(_directory, "config.json");
        var config = new AppConfig();
        var location = new GitHubLocation("o", "corne-config", "main");

        var notice = await GitHubSync.UseAsync(config, configPath, source, location, await source.ListAsync(location), "config/corne.keymap");

        Assert.Null(notice);
        Assert.Equal("corne", config.Zmk.Shield);
        Assert.Equal("cache/zmk/corne@main/app/boards/shields/corne", config.Zmk.ShieldLayoutFolder);

        var loaded = KeymapLoader.Load(config, configPath);
        Assert.Equal(LayoutSource.ZmkRepository, loaded.LayoutSource);
        Assert.Equal("6 Column", loaded.Layout.Name);
    }

    [Fact]
    public async Task FailingToReachZmkStillUsesTheKeymap()
    {
        // ZMK 本体は取れない（何を聞いても 404）。キーマップは使えるので、理由を返して続ける。
        var source = new GitHubSource(new HttpClient(CorneConfig(new FakeGitHub())));
        var configPath = Path.Combine(_directory, "config.json");
        var config = new AppConfig();
        var location = new GitHubLocation("o", "corne-config", "main");

        var notice = await GitHubSync.UseAsync(config, configPath, source, location, await source.ListAsync(location), "config/corne.keymap");

        Assert.Equal(Strings.ZmkShieldFetchFailed(Strings.ZmkShieldNotFound("corne")), notice);
        Assert.Null(config.Zmk.ShieldLayoutFolder);
        Assert.Equal(LayoutSource.Guessed, KeymapLoader.Load(config, configPath).LayoutSource);
    }

    [Fact]
    public async Task ShieldCanBeFetchedByNameForALocalKeymap()
    {
        var keymap = Path.Combine(_directory, "mine", "corne.keymap");
        Directory.CreateDirectory(Path.GetDirectoryName(keymap)!);
        File.Copy(Fixtures.CorneKeymap, keymap);

        var configPath = Path.Combine(_directory, "config.json");
        var config = new AppConfig();
        config.Zmk.KeymapFile = keymap;

        // build.yaml が無いので、名前を言ってもらう必要がある。
        Assert.Null(ZmkShieldSync.SuggestShield(config, configPath));

        var source = new GitHubSource(new HttpClient(ZmkCorne(new FakeGitHub())));
        await Assert.ThrowsAsync<GitHubSourceException>(() => ZmkShieldSync.FetchAsync(config, configPath, source, null));

        await ZmkShieldSync.FetchAsync(config, configPath, source, "Corne_Left");

        Assert.Equal("corne", config.Zmk.Shield);
        Assert.Equal("corne", ZmkShieldSync.SuggestShield(config, configPath));
        Assert.Equal(LayoutSource.ZmkRepository, KeymapLoader.Load(config, configPath).LayoutSource);
    }

    [Fact]
    public async Task ReplacingTheCacheKeepsThePreviousOneWhenTheMoveFails()
    {
        var target = Path.Combine(_directory, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "old.txt"), "old");

        // 取り終えた一時フォルダが無い（入れ替えに失敗する）状況。
        await Assert.ThrowsAnyAsync<IOException>(() =>
            Task.Run(() => GitHubSource.ReplaceFolder(Path.Combine(_directory, "missing"), target)));

        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "old.txt")));
        Assert.False(Directory.Exists(target + ".old"));
    }

    /// <summary>決まった URL にだけ応答を返し、それ以外は 404 にする。</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _bodies = new();
        private readonly Dictionary<string, (HttpStatusCode Status, (string Name, string Value)[] Headers)> _statuses = new();

        public List<string> UserAgents { get; } = new();

        public FakeGitHub Json(string url, string body) => Text(url, body);

        public FakeGitHub Text(string url, string body)
        {
            _bodies[url] = body;
            return this;
        }

        public FakeGitHub Status(string url, HttpStatusCode status, params (string Name, string Value)[] headers)
        {
            _statuses[url] = (status, headers);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UserAgents.Add(request.Headers.UserAgent.ToString());

            var url = request.RequestUri!.AbsoluteUri;
            HttpResponseMessage response;

            if (_statuses.TryGetValue(url, out var status))
            {
                response = new HttpResponseMessage(status.Status);
                foreach (var (name, value) in status.Headers) response.Headers.Add(name, value);
            }
            else
            {
                response = _bodies.TryGetValue(url, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

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
