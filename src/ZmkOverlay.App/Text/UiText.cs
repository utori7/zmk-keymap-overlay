using ZmkOverlay.Core.Text;

namespace ZmkOverlay.App.Text;

/// <summary>
/// UI 側だけで使う文言。仕組みは <see cref="Strings"/> と同じで、日本語と英語を並べて持つ。
/// </summary>
internal static class UiText
{
    private static string T(string ja, string en) => Strings.T(ja, en);

    // ---- アプリ全体 ----

    public static string UnexpectedError => T("予期しないエラー", "Unexpected error");
    public static string StartupFailed => T("起動できませんでした", "Could not start");
    public static string ReloadFailed => T("再読み込みに失敗しました", "Reload failed");

    public static string ErrorDetails(string logPath) => T($"詳細: {logPath}", $"Details: {logPath}");

    public static string KeymapWarnings(int count) =>
        T($"キーマップの読み込みで {count} 件の警告", $"{count} warning(s) while loading the keymap");

    public static string CannotSaveSettings => T("設定を保存できません", "Cannot save settings");


    public static string CannotRegisterHotkey => T("ホットキーを登録できません", "Cannot register the hotkey");
    public static string UnknownCause => T("原因不明", "Unknown cause");
    public static string LayersNotFollowed => T("追従できないレイヤーがあります", "Some layers cannot be followed");

    // ---- トレイ ----

    public static string Disable => T("無効にする", "Disable");
    public static string Enable => T("有効にする", "Enable");

    public static string DisplayMode => T("表示の仕方", "Display mode");
    public static string ModeLayersOnly => T("L1 以上のレイヤーのときだけ表示", "Show only on layer 1 and above");
    public static string ModeLayersOnlyTip =>
        T("普段は出さず、レイヤーキーを押しているあいだだけ表示します。",
          "Hidden normally; shown while a layer key is held.");
    public static string ModeAlways => T("常に表示", "Always show");
    public static string ModeAlwaysTip =>
        T("有効なあいだ出しっぱなしにし、レイヤーに応じて中身を切り替えます。",
          "Stays on screen while enabled and follows the active layer.");

    public static string RunAtLogin => T("Windows のサインイン時に起動する", "Start when I sign in to Windows");
    public static string RunAtLoginTip =>
        T("スタートアップフォルダにショートカットを置きます。", "Places a shortcut in your Startup folder.");

    public static string ShowLayer => T("レイヤーを表示", "Show layer");
    public static string Settings => T("設定…", "Settings…");
    public static string ReloadKeymap => T("キーマップを再読み込み", "Reload keymap");
    public static string Exit => T("終了", "Exit");

    public static string ViewWarnings(int count) => T($"警告 {count} 件を見る…", $"View {count} warning(s)…");
    public static string ClickForAll => T("クリックですべて表示", "Click to see all");
    public static string WarningsTitle => T("キーマップの警告", "Keymap warnings");

    public static string StateEnabled => T("有効", "enabled");
    public static string StateDisabled => T("無効", "disabled");
    public static string OverlayEnabled => T("オーバーレイを有効にしました", "Overlay enabled");
    public static string OverlayDisabled => T("オーバーレイを無効にしました", "Overlay disabled");

    // ---- ホットキー・レイヤー追従 ----

    public static string UnrecognizedKey(string key) =>
        T($"キー '{key}' を解釈できません。", $"Unrecognized key '{key}'.");

    public static string UnrecognizedModifier(string modifier) =>
        T($"修飾キー '{modifier}' を解釈できません。", $"Unrecognized modifier '{modifier}'.");

    public static string HotkeyTaken(string spec) =>
        T($"{spec} は登録できませんでした。他のアプリが使用している可能性があります。",
          $"Could not register {spec}. Another app may be using it.");

    public static string SignalKeyUnrecognized(int layerId, string key) =>
        T($"L{layerId}: キー '{key}' を解釈できません", $"L{layerId}: unrecognized key '{key}'");

    // ---- 自動起動 ----

    public static string ExePathUnknown => T("実行ファイルの場所を特定できません。", "Cannot determine the executable path.");

    public static string ScriptHostUnavailable =>
        T("Windows Script Host を利用できないため、ショートカットを作成できません。",
          "Windows Script Host is unavailable, so the shortcut cannot be created.");

    public static string ScriptHostCreateFailed => T("WScript.Shell を生成できません。", "Cannot create WScript.Shell.");

    public static string ShortcutDescription =>
        T("ZMK のキーマップを画面に重ねて表示します", "Shows your ZMK keymap on top of the screen");

    // ---- オーバーレイ ----

    public static string HintToggle(string toggleHotkey) => T($"{toggleHotkey} オフ", $"{toggleHotkey} off");

    /// <summary><paramref name="range"/> は "0–5" のようなレイヤー番号の範囲。</summary>
    public static string HintLayers(string range) => T($"Ctrl+Alt+{range} レイヤー", $"Ctrl+Alt+{range} layers");

    public static string Combos => T("コンボ", "Combos");

    // ---- 設定画面 ----

    public static string SettingsTitle => T("設定", "Settings");

    public static string PageKeyboard => T("キーボード", "Keyboard");
    public static string PageDisplay => T("表示", "Display");
    public static string PageSync => T("レイヤー追従", "Layer sync");
    public static string PageShortcuts => T("ショートカット", "Shortcuts");
    public static string PageGeneral => T("全般", "General");

    // キーボード
    public static string SampleNote =>
        T("いまはサンプルのキーボード（Pyuron）を表示しています。自分のキーマップ（.keymap）を選んでください。",
          "You are looking at a sample keyboard (Pyuron). Choose your own keymap (.keymap).");

    public static string SectionKeymap => T("PC のファイルから読み込む", "Load from files on this PC");

    public static string SectionGitHub => T("GitHub から読み込む", "Load from GitHub");

    public static string GitHubNote =>
        T("zmk-config のリポジトリの URL を貼り付けて「取得」を押してください（公開リポジトリのみ）。" +
          "ファイルはこの PC に保存され、次からは通信せずに使えます。",
          "Paste the URL of your zmk-config repository and press Fetch (public repositories only). " +
          "Files are saved on this PC, so no network is needed afterwards.");

    public static string GitHubFetch => T("取得", "Fetch");
    public static string GitHubFetching => T("GitHub から取得しています…", "Fetching from GitHub…");
    public static string GitHubKeymapLabel => T("使うキーマップ", "Keymap to use");
    public static string GitHubUse => T("これを使う", "Use this");
    public static string GitHubRefresh => T("取り直す", "Fetch again");

    public static string GitHubChooseKeymap(int count) =>
        T($"キーマップが {count} 個見つかりました。使うものを選んでください。", $"Found {count} keymaps. Choose the one to use.");

    public static string GitHubInUse(string repository, string? branch, string keymapPath) =>
        T($"GitHub の {repository}（{branch ?? "既定のブランチ"}）にある {keymapPath} を使っています。",
          $"Using {keymapPath} from {repository} ({branch ?? "default branch"}) on GitHub.");

    public static string GitHubInvalidUrl =>
        T("GitHub のリポジトリの URL として読めません。例: https://github.com/自分の名前/zmk-config",
          "This does not look like a GitHub repository URL. Example: https://github.com/your-name/zmk-config");

    public static string GitHubRefreshFailed =>
        T("GitHub から取り直せませんでした。保存済みのファイルで続けます", "Could not fetch from GitHub. Continuing with the saved files");
    public static string KeymapFileLabel => T("キーマップ", "Keymap");
    public static string LayoutFileLabel => T("物理レイアウト", "Physical layout");
    public static string Browse => T("選ぶ…", "Browse…");
    public static string UseKeymapLayout => T("自動で探す", "Find automatically");
    public static string SampleKeymap => T("サンプル（Pyuron）", "Sample (Pyuron)");
    public static string LayoutInKeymap => T("キーマップの中に書かれていたもの", "Defined in the keymap");
    public static string LayoutFoundNearby(string path) => T($"自動で見つけたもの: {path}", $"Found automatically: {path}");

    public static string LayoutGuessedLabel =>
        T("キーマップの並びから推定（配置が違うときはファイルを選んでください）",
          "Guessed from the keymap (choose a file if it looks wrong)");
    public static string ChooseKeymap => T("キーマップを選ぶ", "Choose a keymap");
    public static string ChooseLayout => T("物理レイアウトを選ぶ", "Choose the physical layout");

    public static string KeymapFilter =>
        T("ZMK キーマップ (*.keymap)|*.keymap|すべてのファイル (*.*)|*.*",
          "ZMK keymap (*.keymap)|*.keymap|All files (*.*)|*.*");

    public static string LayoutFilter =>
        T("devicetree (*.dtsi;*.overlay;*.keymap)|*.dtsi;*.overlay;*.keymap|すべてのファイル (*.*)|*.*",
          "Devicetree (*.dtsi;*.overlay;*.keymap)|*.dtsi;*.overlay;*.keymap|All files (*.*)|*.*");

    public static string LayoutNeeded =>
        T("このキーマップにはキーの並び（物理レイアウト）が含まれていません。続けて、シールドの .dtsi を選んでください。",
          "This keymap does not describe where the keys are (physical layout). Next, choose your shield's .dtsi file.");

    public static string SectionHostLayout => T("PC のキーボード配列", "Keyboard layout on this PC");
    public static string HostJis => T("日本語配列（JIS）", "Japanese (JIS)");
    public static string HostUs => T("英語配列（US）", "English (US)");

    public static string HostLayoutNote =>
        T("キーボードが送るキーは同じでも、PC の配列設定によって出る記号が変わります。Windows の設定に合わせてください。",
          "The same key can type different symbols depending on this PC's layout. Match your Windows setting.");

    public static string SectionLayerNames => T("レイヤー名", "Layer names");
    public static string LayerNamesNote => T("空にすると、キーマップに書かれた名前に戻ります。", "Leave empty to use the name from the keymap.");
    public static string SampleCannotChange => T("サンプルのキーマップでは変更できません。", "Cannot be changed for the sample keymap.");

    public static string SectionWarnings => T("読み込みの警告", "Loading warnings");
    public static string NoWarnings => T("警告はありません。", "No warnings.");

    // 表示
    public static string SectionShowWhen => T("表示するとき", "When to show");
    public static string SectionLook => T("見た目", "Appearance");
    public static string SizeLabel => T("大きさ", "Size");
    public static string OpacityLabel => T("不透明度", "Opacity");
    public static string PositionLabel => T("位置", "Position");
    public static string MarginLabel => T("画面端からの余白", "Distance from the screen edge");
    public static string SectionPreview => T("プレビュー", "Preview");

    public static string PositionName(string position) => position switch
    {
        "BottomLeft" => T("下・左", "Bottom left"),
        "BottomRight" => T("下・右", "Bottom right"),
        "TopCenter" => T("上・中央", "Top center"),
        "TopLeft" => T("上・左", "Top left"),
        "TopRight" => T("上・右", "Top right"),
        "Center" => T("画面の中央", "Center of the screen"),
        _ => T("下・中央", "Bottom center"),
    };

    // レイヤー追従
    public static string SyncExplain =>
        T("キーボードがレイヤーに入るときに送る合図キー（F13〜F24）を受け取って、表示を切り替えます。そのためにキーボード側の設定変更が必要です。",
          "When the keyboard enters a layer it sends a signal key (F13–F24), and the overlay follows it. This needs a change on the keyboard side.");

    public static string SyncEnabled => T("キーボードのレイヤーに追従する", "Follow the keyboard's layers");
    public static string SyncHold => T("キーを押しているあいだだけ表示", "Show while the key is held");
    public static string SyncToggle => T("押すたびに表示と非表示を切り替える", "Toggle on each press");
    public static string SectionSignalKeys => T("合図キー", "Signal keys");
    public static string ColumnLayer => T("レイヤー", "Layer");
    public static string ColumnSignal => T("合図キー", "Signal key");
    public static string ColumnTest => T("テスト", "Test");
    public static string NoSignal => T("なし", "None");

    public static string SignalAutoNote =>
        T("キーマップに合図キーが仕込まれていれば、自動で読み取って表に入ります。ここで変えると、そのレイヤーだけ設定が優先されます。",
          "Signal keys built into your keymap are detected and filled in automatically. Changing one here overrides it for that layer.");

    public static string DetectedFromKeymap(string key) =>
        T($"キーマップから読み取った値: {key}", $"Detected in the keymap: {key}");

    public static string SignalTestHint =>
        T("キーボードでレイヤーキーを押してみてください。合図を受け取ったレイヤーに ✓ が付きます。",
          "Press a layer key on your keyboard. Layers whose signal arrived get a ✓.");

    // ショートカット
    public static string ToggleHotkeyLabel => T("オーバーレイの有効 / 無効", "Enable / disable the overlay");

    public static string HotkeyHint =>
        T("欄をクリックしてから、使いたい組み合わせを押してください。Esc で取り消し、レイヤーの欄は Delete で割り当てを外します。",
          "Click a box, then press the combination you want. Esc cancels; Delete removes a layer's shortcut.");

    public static string PressKeys => T("組み合わせを押してください…", "Press a combination…");
    public static string NeedModifier => T("Ctrl・Alt・Shift・Win のどれかと一緒に押してください", "Hold Ctrl, Alt, Shift or Win with the key");
    public static string ManualKeys => T("ショートカットでレイヤーを表示する", "Show layers with shortcuts");

    public static string ManualKeysNote =>
        T("キーボードを書き換えていなくても、手でレイヤーを出せます。数字キーの無いキーボードでは、押しやすい組み合わせに変えてください。",
          "Lets you show layers by hand, even before changing your keyboard. If your keyboard has no number keys, pick combinations you can press.");

    public static string SectionLayerShortcuts => T("レイヤーを手で表示する", "Show a layer by hand");
    public static string NoShortcut => T("なし", "None");
    public static string ResetToDefault => T("既定に戻す", "Reset");

    public static string ShortcutConflict(string spec, string usedFor) =>
        T($"{spec} は「{usedFor}」に使われています", $"{spec} is already used for \"{usedFor}\"");

    public static string LayerShortcutUse(int layerId) => T($"L{layerId} の表示", $"showing L{layerId}");
    public static string SignalKeyUse(int layerId) => T($"L{layerId} の合図キー", $"the signal key of L{layerId}");

    // 全般
    public static string LanguageLabel => T("表示言語", "Language");
    public static string LanguageAuto => T("自動（Windows に合わせる）", "Automatic (follow Windows)");
    public static string SectionAbout => T("このアプリについて", "About");
    public static string ConfigFileLabel => T("設定ファイル", "Settings file");
    public static string OpenFolder => T("フォルダを開く", "Open folder");
    public static string VersionLabel => T("バージョン", "Version");
}
