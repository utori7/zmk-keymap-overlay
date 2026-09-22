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

    /// <summary>修飾キーを含むか（"ctrl" / "alt" / "shift" / "win"。表記の揺れは吸収する）。</summary>
    public bool Has(string modifier) => NormalizedModifiers().Contains(modifier);

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
/// ショートカットとして使ってよい組み合わせの決まり。
///
/// Ctrl+Alt は多くの欧州配列で AltGr と同じ扱いになり、ドイツ語配列の Ctrl+Alt+7 は「{」を打つ。
/// そういう組み合わせをホットキーとして押さえると、その文字が打てなくなる。
/// 文字が出るかどうかは Windows の配列情報でしか分からないので、判定はアプリが起動時に差し込む。
/// </summary>
public static class HotkeyRules
{
    /// <summary>その組み合わせを押すと、入っているいずれかの配列で文字が入力されるか。既定は「入力されない」。</summary>
    public static Func<HotkeySpec, bool> TypesCharacter { get; set; } = _ => false;

    /// <summary>レイヤーを手で表示する既定のショートカット（Ctrl+Alt+番号、0〜9 のみ）。</summary>
    public static HotkeySpec? DefaultLayerHotkey(int layerId) =>
        layerId is >= 0 and <= 9
            ? new HotkeySpec { Modifiers = { "Ctrl", "Alt" }, Key = layerId.ToString(CultureInfo.InvariantCulture) }
            : null;

    /// <summary>有効 / 無効の切り替えの既定の候補。先頭から、文字入力に使われていないものを選ぶ。</summary>
    public static IReadOnlyList<HotkeySpec> ToggleCandidates { get; } = new[]
    {
        new HotkeySpec { Modifiers = { "Ctrl", "Alt" }, Key = "K" },
        new HotkeySpec { Modifiers = { "Ctrl", "Alt", "Shift" }, Key = "K" },
        new HotkeySpec { Modifiers = { "Ctrl", "Alt" }, Key = "F12" },
    };

    public static HotkeySpec ChooseDefaultToggle() =>
        Copy(ToggleCandidates.FirstOrDefault(s => !TypesCharacter(s)) ?? ToggleCandidates[0]);

    private static HotkeySpec Copy(HotkeySpec spec) => new() { Modifiers = spec.Modifiers.ToList(), Key = spec.Key };
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

    /// <summary>
    /// ZMK 本体から取ってきたシールド定義の保存先（設定ファイルからの相対でよい）。
    /// Corne のように定義が ZMK 本体にあるキーボードで、キーマップの近くに物理レイアウトが無いときに使う。
    /// </summary>
    public string? ShieldLayoutFolder { get; set; }

    /// <summary><see cref="ShieldLayoutFolder"/> のシールド名（例 "corne"）。画面に出すのと、取り直すときに使う。</summary>
    public string? Shield { get; set; }

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

/// <summary><see cref="AppConfig.DisplayMode"/> に書く値。</summary>
public static class DisplayModes
{
    /// <summary>L1 以上のレイヤーにいるあいだだけ表示する。L0 では出ない。</summary>
    public const string LayersOnly = "layersOnly";

    /// <summary><see cref="AppConfig.HiddenLayers"/> に無いレイヤーにいるあいだだけ表示する。L0 も選べる。</summary>
    public const string SelectedLayers = "selectedLayers";

    /// <summary>常に表示し、レイヤーに応じて中身が切り替わる。</summary>
    public const string Always = "always";
}

public sealed class AppConfig
{
    /// <summary>設定ファイルからの相対パス。既定は同梱のサンプル。</summary>
    public string LayoutFile { get; set; } = ConfigPaths.SampleLayoutFile;
    public string KeymapFile { get; set; } = ConfigPaths.SampleKeymapFile;

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
    /// 無効のあいだは、どの値でも一切表示しない。
    ///
    /// "layersOnly"     L1 以上のレイヤーにいるあいだだけ表示する。L0 では出ない。
    /// "selectedLayers" <see cref="HiddenLayers"/> に無いレイヤーにいるあいだだけ表示する。
    /// "always"         常に表示し、レイヤーに応じて中身が切り替わる。
    ///
    /// 知らない値は "layersOnly" として扱う。
    /// </summary>
    public string DisplayMode { get; set; } = DisplayModes.LayersOnly;

    [JsonIgnore]
    public bool IsAlwaysVisible =>
        string.Equals(DisplayMode, DisplayModes.Always, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsSelectedLayers =>
        string.Equals(DisplayMode, DisplayModes.SelectedLayers, StringComparison.OrdinalIgnoreCase);

    /// <summary><see cref="DisplayMode"/> を <see cref="DisplayModes"/> のどれかに揃えたもの。</summary>
    [JsonIgnore]
    public string EffectiveDisplayMode =>
        IsAlwaysVisible ? DisplayModes.Always
        : IsSelectedLayers ? DisplayModes.SelectedLayers
        : DisplayModes.LayersOnly;

    /// <summary>
    /// "selectedLayers" のときに表示しないレイヤーの番号。よく使って覚えたレイヤーを入れておくと、
    /// そのレイヤーに入っても出ないので気が散らない。
    ///
    /// 表示する側ではなく表示しない側を持つのは、キーマップにあとから足したレイヤーを表示するため。
    /// 新しいレイヤーはまだ覚えていない。
    /// 既定の [0] は "layersOnly" と同じ動き。初めてこの見せ方を選んだときは、そこから外していく。
    /// </summary>
    public List<int> HiddenLayers { get; set; } = new() { 0 };

    /// <summary>
    /// そのレイヤーにいるときにオーバーレイを出すか。
    /// 何も押していないときは <paramref name="layerId"/> に <paramref name="baseLayerId"/> を渡す。
    /// 手で選んだレイヤー（Ctrl+Alt+番号）は明示的な操作なので、これに関係なく出す。
    /// </summary>
    public bool ShowsLayer(int layerId, int baseLayerId)
    {
        if (IsAlwaysVisible) return true;
        if (IsSelectedLayers) return !HiddenLayers.Contains(layerId);
        return layerId != baseLayerId;
    }

    /// <summary>
    /// "BottomCenter" | "BottomLeft" | "BottomRight" | "TopCenter" | "TopLeft" | "TopRight" | "Center"
    /// </summary>
    public string Position { get; set; } = "BottomCenter";

    /// <summary>画面端からの余白（ピクセル）。Center では無視される。</summary>
    public double Margin { get; set; } = 48;

    /// <summary>
    /// <see cref="Position"/> で決まる位置からのずれ（ピクセル）。オーバーレイをドラッグで動かすと入る。
    ///
    /// 置いた場所そのものではなく、基準からのずれで持つ。カーソルのあるモニタに出すという動きを保ったまま、
    /// 解像度の違うモニタでも同じような場所に出せるため。基準を選び直したときは捨てる（設定画面）。
    /// </summary>
    public double OffsetX { get; set; }

    /// <inheritdoc cref="OffsetX"/>
    public double OffsetY { get; set; }

    /// <summary>ドラッグで動かした結果、基準の位置から離れているか。</summary>
    [JsonIgnore]
    public bool HasOffset => OffsetX != 0 || OffsetY != 0;

    /// <summary>
    /// クリックを下のアプリへ素通しするか。
    ///
    /// 既定は素通し。タイピング中に視界の端に出す道具なので、普段は邪魔をしないのが正しい。
    /// false にすると、オーバーレイがクリックとドラッグを受け取る（タブでレイヤーを選ぶ・板を動かす）。
    /// 受け取るあいだも、フォーカスは奪わない（WS_EX_NOACTIVATE は常時）。
    /// </summary>
    public bool ClickThrough { get; set; } = true;

    /// <summary>オーバーレイがクリックとドラッグを受け取る状態か。画面側で毎回否定を書かずに済むように。</summary>
    [JsonIgnore]
    public bool IsInteractive => !ClickThrough;

    /// <summary>"jis" | "us"。ラベル解決に使う（Phase 1 以降）。</summary>
    public string KeyboardLayout { get; set; } = "jis";

    /// <summary>"auto" | "ja" | "en"。auto は Windows の表示言語に従う。</summary>
    public string Language { get; set; } = "auto";

    public HotkeySpec ToggleHotkey { get; set; } =
        new() { Modifiers = { "Ctrl", "Alt" }, Key = "K" };

    /// <summary>
    /// <see cref="ClickThrough"/> を切り替えるショートカット。key が空なら割り当てない（既定）。
    ///
    /// 既定を決めないのは、押さえた組み合わせがその人の環境で使えるとは限らないため。
    /// 数字キーの無いキーボードもあるので、数字前提の既定も作らない。
    /// </summary>
    public HotkeySpec ClickThroughHotkey { get; set; } = new();

    /// <summary>クリックの受け取りを切り替えるショートカット。割り当てが無いなら null。</summary>
    [JsonIgnore]
    public HotkeySpec? EffectiveClickThroughHotkey =>
        string.IsNullOrWhiteSpace(ClickThroughHotkey.Key) ? null : ClickThroughHotkey;

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

    /// <summary>
    /// そのレイヤーを手で表示するショートカット。使わない・割り当てが無いなら null。
    /// 既定の Ctrl+Alt+番号 が、この PC の配列で文字の入力に使われるなら割り当てない（<see cref="HotkeyRules"/>）。
    /// </summary>
    public HotkeySpec? ManualLayerHotkey(int layerId)
    {
        if (!EnableManualLayerKeys) return null;

        if (LayerHotkeys.TryGetValue(layerId.ToString(CultureInfo.InvariantCulture), out var custom))
            return string.IsNullOrWhiteSpace(custom.Key) ? null : custom;

        return HotkeyRules.DefaultLayerHotkey(layerId) is { } fallback && !HotkeyRules.TypesCharacter(fallback)
            ? fallback
            : null;
    }

    /// <summary>レイヤーのショートカットが、利用者が選んだものではなく既定の Ctrl+Alt+番号 か。</summary>
    public bool UsesDefaultLayerHotkey(int layerId) =>
        EnableManualLayerKeys && !LayerHotkeys.ContainsKey(layerId.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// 初期設定の案内を、次はどのステップから開くか。途中で閉じたときのステップが入り、
    /// 「完了」まで見たら消える。入っているあいだ、トレイのメニューは「続ける」と出す。
    ///
    /// 設定ではなく案内の途中の記録だが、PC を再起動しても続きから開けるように設定ファイルに持つ。
    /// 「やり直す」しか入口が無いと、途中まで進めた人が消えると思って押せない。
    /// </summary>
    public int? SetupStep { get; set; }

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
