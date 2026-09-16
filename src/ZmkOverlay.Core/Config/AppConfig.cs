using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZmkOverlay.Core.Config;

public sealed class HotkeySpec
{
    /// <summary>"Ctrl", "Alt", "Shift", "Win" の組み合わせ。空なら修飾なし。</summary>
    public List<string> Modifiers { get; set; } = new();

    /// <summary>"K", "F13", "Right" など。</summary>
    public string Key { get; set; } = "";

    public override string ToString() =>
        Modifiers.Count == 0 ? Key : string.Join("+", Modifiers) + "+" + Key;

    /// <summary>
    /// 同じ組み合わせか。修飾キーの順番・大文字小文字・Control / Ctrl のような表記の揺れは無視する。
    /// 設定画面で、すでに使われている組み合わせを選ばせないために使う。
    /// </summary>
    public bool SameAs(HotkeySpec other) =>
        string.Equals(Key.Trim(), other.Key.Trim(), StringComparison.OrdinalIgnoreCase)
        && NormalizedModifiers().SetEquals(other.NormalizedModifiers());

    private HashSet<string> NormalizedModifiers() =>
        Modifiers
            .Select(m => m.Trim().ToLowerInvariant() switch
            {
                "control" => "ctrl",
                "windows" => "win",
                var name => name,
            })
            .ToHashSet();
}

/// <summary>
/// キーボードのレイヤーへの自動追従。詳細は DESIGN.md「レイヤー連動（案A）」。
/// </summary>
public sealed class LayerSyncConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// "hold"   合図キーを押している間だけ表示する（既定）。
    /// "toggle" 合図キーを受け取るたびに表示 / 非表示を切り替える。
    /// </summary>
    public string Mode { get; set; } = "hold";

    /// <summary>
    /// 合図キーが離されたかを見に行く間隔。
    /// 検証（DESIGN.md「V1 の検証結果」）のとおり、これより細かくしても
    /// Windows のタイマ分解能に埋もれて意味がない。
    /// </summary>
    public int PollIntervalMs { get; set; } = 15;

    /// <summary>
    /// 押下を一度も観測できないまま、この時間が過ぎたら離されたとみなす。
    /// 取りこぼしたときにオーバーレイが出しっぱなしになるのを防ぐ保険。
    /// </summary>
    public int GraceMs { get; set; } = 150;

    [JsonIgnore]
    public bool IsHoldMode => !string.Equals(Mode, "toggle", StringComparison.OrdinalIgnoreCase);
}

/// <summary>GitHub の zmk-config から読むときの取得元。</summary>
public sealed class GitHubSourceConfig
{
    /// <summary>"owner/name"。</summary>
    public string Repository { get; set; } = "";

    /// <summary>ブランチ。空なら既定のブランチ。</summary>
    public string? Branch { get; set; }

    /// <summary>リポジトリ内のキーマップのパス（例 "config/corne.keymap"）。</summary>
    public string? KeymapPath { get; set; }
}

/// <summary>
/// ZMK のソースから直接読むときの設定。<see cref="KeymapFile"/> が空なら
/// 手書き JSON（<see cref="AppConfig.LayoutFile"/> ほか）を使う。
/// </summary>
public sealed class ZmkSourceConfig
{
    /// <summary><c>.keymap</c> ファイル。設定ファイルからの相対パスでよい。</summary>
    public string? KeymapFile { get; set; }

    /// <summary>
    /// 物理レイアウトを持つファイル。シールドの <c>.dtsi</c> に分かれている
    /// 構成では指定が必要。省略時はキーマップ側から探す。
    /// </summary>
    public string? PhysicalLayoutFile { get; set; }

    /// <summary>キーコード名の表示差し替え。例 <c>{"INT4": "かな"}</c>。</summary>
    public Dictionary<string, string> LabelOverrides { get; set; } = new();

    /// <summary>レイヤー番号 → 表示名の差し替え。例 <c>{"0": "BASE"}</c>。</summary>
    public Dictionary<string, string> LayerNames { get; set; } = new();

    /// <summary>レイヤー番号 → 合図キー。例 <c>{"1": "F13"}</c>。</summary>
    public Dictionary<string, string> SignalKeys { get; set; } = new();

    /// <summary>
    /// どこから読むか。"local"（既定）は <see cref="KeymapFile"/> をそのまま読む。
    /// "github" は <see cref="GitHub"/> のリポジトリから取ってきた保存分を読む。
    /// そのときも <see cref="KeymapFile"/> は保存先を指しているので、読み込み自体は同じ。
    /// </summary>
    public string Source { get; set; } = "local";

    /// <summary>GitHub から読むときの取得元。取り直すときに使う。</summary>
    [JsonPropertyName("github")]
    public GitHubSourceConfig GitHub { get; set; } = new();

    [JsonIgnore]
    public bool IsGitHub =>
        string.Equals(Source, "github", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(GitHub.Repository);

    /// <summary>
    /// モデルに無いキーを保持しておくための入れ物。設定ファイルには
    /// <c>_comment</c> のような注釈が書かれていることがあり、
    /// 書き戻すときにそれを消してしまわないようにする。
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public bool IsEnabled => !string.IsNullOrWhiteSpace(KeymapFile);

    public Dictionary<int, string> ParseIndexed(Dictionary<string, string> source)
    {
        var result = new Dictionary<int, string>();

        foreach (var (key, value) in source)
            if (int.TryParse(key, out var index)) result[index] = value;

        return result;
    }
}

public sealed class AppConfig
{
    /// <summary>設定ファイルからの相対パス。</summary>
    public string LayoutFile { get; set; } = "data/layouts/pyuron.json";
    public string KeymapFile { get; set; } = "data/keymaps/pyuron.json";

    /// <summary>ZMK のソースから直接読む場合の設定。</summary>
    public ZmkSourceConfig Zmk { get; set; } = new();

    /// <summary>
    /// キー 1u を何ピクセルで描くか。96dpi 基準。
    /// タイピング中に視界を塞がない大きさを既定にしてある。
    /// </summary>
    public double KeyUnitPx { get; set; } = 44;

    /// <summary>オーバーレイ全体の不透明度。</summary>
    public double Opacity { get; set; } = 0.88;

    /// <summary>
    /// ホットキーで有効にしているあいだ、どう見せるか。
    /// 無効のあいだは、どちらの値でも一切表示しない。
    ///
    /// "layersOnly" L1 以上のレイヤーにいるあいだだけ表示する。L0 では出ない。
    /// "always"     常に表示し、レイヤーに応じて中身が切り替わる。
    /// </summary>
    public string DisplayMode { get; set; } = "layersOnly";

    [JsonIgnore]
    public bool IsAlwaysVisible =>
        string.Equals(DisplayMode, "always", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// "BottomCenter" | "BottomLeft" | "BottomRight" | "TopCenter" | "TopLeft" | "TopRight" | "Center"
    /// </summary>
    public string Position { get; set; } = "BottomCenter";

    /// <summary>画面端からの余白（ピクセル）。Center では無視される。</summary>
    public double Margin { get; set; } = 48;

    /// <summary>"jis" | "us"。ラベル解決に使う（Phase 1 以降）。</summary>
    public string KeyboardLayout { get; set; } = "jis";

    /// <summary>"auto" | "ja" | "en"。auto は Windows の表示言語に従う。</summary>
    public string Language { get; set; } = "auto";

    public HotkeySpec ToggleHotkey { get; set; } =
        new() { Modifiers = { "Ctrl", "Alt" }, Key = "K" };

    public LayerSyncConfig LayerSync { get; set; } = new();

    /// <summary>
    /// レイヤーを手で表示するショートカットを使うか。キーボードを書き換える前や、
    /// 合図キーを持たないレイヤーを見たいときの逃げ道。割り当ては <see cref="LayerHotkeys"/>。
    /// </summary>
    public bool EnableManualLayerKeys { get; set; } = true;

    /// <summary>
    /// レイヤー番号 → そのレイヤーを手で表示するショートカット。
    /// 書かれていないレイヤーは Ctrl+Alt+番号（0〜9 のみ）を使う。key を空にすると割り当てない。
    ///
    /// 数字キーを持たない自作キーボードでも使えるように、好きな組み合わせにできる。
    /// </summary>
    public Dictionary<string, HotkeySpec> LayerHotkeys { get; set; } = new();

    /// <summary>そのレイヤーを手で表示するショートカット。使わない・割り当てが無いなら null。</summary>
    public HotkeySpec? ManualLayerHotkey(int layerId)
    {
        if (!EnableManualLayerKeys) return null;

        if (LayerHotkeys.TryGetValue(layerId.ToString(CultureInfo.InvariantCulture), out var custom))
            return string.IsNullOrWhiteSpace(custom.Key) ? null : custom;

        return layerId is >= 0 and <= 9
            ? new HotkeySpec { Modifiers = { "Ctrl", "Alt" }, Key = layerId.ToString(CultureInfo.InvariantCulture) }
            : null;
    }

    /// <summary>モデルに無いキーの保持。<see cref="ZmkSourceConfig.Extra"/> と同じ目的。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },

        // 既定では日本語が かな のように退避される。設定ファイルは
        // 利用者が手で開いて編集するものなので、そのまま読める形で書き出す。
        // 「Unsafe」は HTML に埋め込む場合の話で、ローカルの設定ファイルには関係ない。
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonSerializerOptions SerializerOptions => JsonOptions;

    /// <summary>
    /// 設定画面で書き換えるための複製。反映に失敗したら複製を捨てるだけで済むよう、
    /// いま効いている設定そのものには触らない。ファイルと同じ経路で複製するので注釈（Extra）も引き継ぐ。
    /// </summary>
    public AppConfig Clone() =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    public static AppConfig Load(string path) =>
        JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOptions)
        ?? new AppConfig();

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
