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

    public static string KeymapNotLoaded => T("キーマップを読み込めませんでした", "Could not load your keymap");

    public static string StartupFellBack(string reason) =>
        T($"キーマップを読み込めなかったので、サンプルを表示しています（{reason}）。キーマップの場所を選び直してください。",
          $"Your keymap could not be loaded, so the sample is shown ({reason}). Please choose your keymap again.");

    public static string SettingsFileReset => T("設定ファイルを作り直しました", "The settings file was recreated");

    public static string ConfigWasBroken(string movedTo, string reason) =>
        T($"設定ファイルを読めなかったので（{reason}）、{movedTo} に移して初めからやり直します。",
          $"The settings file could not be read ({reason}). It was moved to {movedTo}, and setup starts over.");

    public static string ZmkFetchProblem => T("キーの並びを取得できませんでした", "Could not get the key positions");

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

    public static string HotkeyTypesCharacter(string spec) =>
        T($"{spec} はこの PC のキーボード配列で文字の入力に使われるので、登録しませんでした。設定画面の「ショートカット」で別の組み合わせを選んでください。",
          $"{spec} types a character with this PC's keyboard layout, so it was not registered. Choose another combination in Settings → Shortcuts.");

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
        T("いまはサンプルのキーボード（Corne）を表示しています。自分のキーマップ（.keymap）を選んでください。",
          "You are looking at a sample keyboard (Corne). Choose your own keymap (.keymap).");

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
    public static string SampleKeymap => T("サンプル（Corne）", "Sample (Corne)");
    public static string LayoutInKeymap => T("キーマップの中に書かれていたもの", "Defined in the keymap");
    public static string LayoutFoundNearby(string path) => T($"自動で見つけたもの: {path}", $"Found automatically: {path}");

    public static string LayoutFromZmk(string? shield) =>
        T($"ZMK 本体から取得したもの（{shield}）", $"Downloaded from ZMK ({shield})");

    public static string LayoutGuessedLabel =>
        T("キーマップの並びから推定（配置が違うときは「ZMK 本体から取得」を試してください）",
          "Guessed from the keymap (try \"Get from ZMK\" if it looks wrong)");

    // ZMK 本体からキーの並びを取る
    public static string SectionZmkLayout => T("ZMK 本体からキーの並びを取得", "Get key positions from ZMK");

    public static string ZmkLayoutNote =>
        T("Corne・Lily58・Sofle など、ZMK 本体に定義があるキーボード向けです。zmk-config の build.yaml の shield に書かれている名前" +
          "（例: corne。_left / _right は付けなくて構いません）を入れて押してください。通信するのは、このボタンを押したときだけです。",
          "For keyboards defined in ZMK itself, such as Corne, Lily58 and Sofle. Enter the shield name from your zmk-config's build.yaml " +
          "(for example: corne; _left / _right can be left out) and press the button. The app goes online only when you press it.");

    public static string ZmkShieldLabel => T("シールド名", "Shield name");
    public static string ZmkFetch => T("ZMK 本体から取得", "Get from ZMK");
    public static string ZmkFetching => T("ZMK 本体から取得しています…", "Downloading from ZMK…");

    public static string ZmkFetched(string? shield) =>
        T($"ZMK 本体の {shield} のキーの並びを使っています。", $"Using the key positions of {shield} from ZMK.");

    public static string ZmkFetchedButNotUsed =>
        T("取得しましたが、キーの数が合わないため使っていません。シールド名を確認してください。",
          "Downloaded, but the number of keys does not match, so it is not used. Check the shield name.");
    public static string ChooseKeymap => T("キーマップを選ぶ", "Choose a keymap");
    public static string ChooseLayout => T("物理レイアウトを選ぶ", "Choose the physical layout");

    public static string KeymapFilter =>
        T("ZMK キーマップ (*.keymap)|*.keymap|すべてのファイル (*.*)|*.*",
          "ZMK keymap (*.keymap)|*.keymap|All files (*.*)|*.*");

    public static string LayoutFilter =>
        T("devicetree (*.dtsi;*.overlay;*.keymap)|*.dtsi;*.overlay;*.keymap|すべてのファイル (*.*)|*.*",
          "Devicetree (*.dtsi;*.overlay;*.keymap)|*.dtsi;*.overlay;*.keymap|All files (*.*)|*.*");

    public static string LayoutNeeded =>
        T("このキーマップからはキーの並び（物理レイアウト）が分かりませんでした。続けて、キーの並びが書かれた .dtsi を選んでください。",
          "The key positions (physical layout) could not be worked out from this keymap. Next, choose a .dtsi file that describes them.");

    public static string LayoutNeededAskZmk(string shield) =>
        T($"このキーマップからはキーの並び（物理レイアウト）が分かりませんでした。\n\n" +
          $"build.yaml によると、キーボードは {shield} です。ZMK 本体からキーの並びを取得しますか？（インターネットに接続します）\n\n" +
          "「いいえ」を選ぶと、キーの並びが書かれた .dtsi を選べます。",
          $"The key positions (physical layout) could not be worked out from this keymap.\n\n" +
          $"According to build.yaml, the keyboard is {shield}. Download its key positions from ZMK? (This goes online.)\n\n" +
          "Choose \"No\" to pick a .dtsi file that describes them instead.");

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

    public static string OpenFirmwareSetup => T("キーボードを書き換える（試験的）…", "Update your keyboard (experimental)…");

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

    public static string ShortcutTypesCharacter(string spec) =>
        T($"{spec} はこの PC の配列で文字の入力に使われます。別の組み合わせを押してください",
          $"{spec} types a character with this PC's layout. Press another combination");

    public static string LayerShortcutUse(int layerId) => T($"L{layerId} の表示", $"showing L{layerId}");
    public static string SignalKeyUse(int layerId) => T($"L{layerId} の合図キー", $"the signal key of L{layerId}");

    // 全般
    public static string LanguageLabel => T("表示言語", "Language");
    public static string LanguageAuto => T("自動（Windows に合わせる）", "Automatic (follow Windows)");
    public static string SectionAbout => T("このアプリについて", "About");
    public static string ConfigFileLabel => T("設定ファイル", "Settings file");
    public static string OpenFolder => T("フォルダを開く", "Open folder");
    public static string VersionLabel => T("バージョン", "Version");

    // ---- 初期設定 ----

    public static string SetupTitle => T("はじめての設定", "Setup");
    public static string SetupMenu => T("初期設定をやり直す…", "Run setup again…");
    public static string SetupStepOf(int step, int total) => T($"ステップ {step} / {total}", $"Step {step} of {total}");
    public static string SetupNext => T("次へ", "Next");
    public static string SetupBack => T("戻る", "Back");
    public static string SetupLater => T("あとで", "Later");
    public static string SetupFinish => T("完了", "Finish");

    public static string WelcomeTitle => T("ZMK Keymap Overlay へようこそ", "Welcome to ZMK Keymap Overlay");

    public static string WelcomeLead =>
        T("ZMK キーボードのキーマップを、画面の端に半透明で表示するアプリです。" +
          "レイヤーキーを押しているあいだ、そのレイヤーの配置が出ます。",
          "This app shows your ZMK keyboard's keymap on the edge of the screen. " +
          "While you hold a layer key, that layer's keys appear.");

    public static string WelcomeSteps =>
        T("これから次の順に準備します（5 分ほど。キーボードを書き換える場合は、ビルドを待つ時間が加わります）。\n" +
          "  1. キーマップの場所を教える\n" +
          "  2. キーボードの形を確かめる\n" +
          "  3. キーボードがレイヤーの合図を送るようにする\n" +
          "  4. 動作を確かめる",
          "We'll get ready in this order (about 5 minutes, plus build time if you update your keyboard):\n" +
          "  1. Tell the app where your keymap is\n" +
          "  2. Check the shape of your keyboard\n" +
          "  3. Make your keyboard send layer signals\n" +
          "  4. Try it out");

    public static string SourceTitle => T("キーマップはどこにありますか？", "Where is your keymap?");
    public static string SourceLead => T("ZMK の設定（zmk-config）がある場所を選んでください。", "Choose where your ZMK configuration (zmk-config) is.");
    public static string ChoiceGitHubTitle => T("GitHub にある", "On GitHub");

    public static string ChoiceGitHubNote =>
        T("ブラウザで開いている zmk-config のアドレスを貼り付けて「取得」を押します（公開リポジトリのみ）。",
          "Paste the address of your zmk-config page and press Fetch (public repositories only).");

    public static string ChoiceLocalTitle => T("この PC にファイルがある", "On this PC");

    public static string ChoiceLocalNote =>
        T(".keymap ファイルを選びます。キーの並び（物理レイアウト）は同じフォルダから自動で探し、無ければ推定します。",
          "Choose your .keymap file. The key positions (physical layout) are looked up in the same folder, or guessed.");

    public static string ChoiceSampleTitle => T("まずはサンプルで試す", "Just try a sample");

    public static string ChoiceSampleNote =>
        T("定番の分割キーボード Corne の配置で、見た目と操作を確かめます。あとから自分のものに変えられます。",
          "See how it looks with Corne, a popular split keyboard. You can switch to your own later.");

    public static string UseSample => T("サンプルを使う", "Use the sample");
    public static string SourceCurrent(string what) => T($"いまの読み込み元: {what}", $"Current source: {what}");

    public static string ShapeTitle => T("キーボードの形は合っていますか？", "Does this look like your keyboard?");

    public static string ShapeLead =>
        T("キーの並びが実物と違うときは、下の「ZMK 本体から取得」を試すか、キーの並びが書かれた .dtsi を選んでください。" +
          "PC の配列（JIS / US）もここで合わせます。",
          "If the key positions don't match, try \"Get from ZMK\" below, or choose a .dtsi file that describes them. " +
          "Set this PC's keyboard layout (JIS / US) here too.");

    public static string ShapeSourceLabel(string what) => T($"キーの並び: {what}", $"Key positions: {what}");
    public static string ShapeBrowse => T("物理レイアウトを選ぶ…", "Choose physical layout…");

    public static string FirmwareTitle => T("キーボードから合図を送れるようにする", "Let your keyboard send layer signals");

    public static string FirmwareLeadNeeded =>
        T("レイヤーに入ったことを PC に知らせるには、キーボードのファームを少し書き換える必要があります。" +
          "書き換え済みのキーマップをアプリが用意しました。普段使わない F13〜F24 を合図に使い、" +
          "このアプリが動いている PC では、ほかのアプリに届きません。",
          "To tell the PC which layer you're on, your keyboard firmware needs a small change. " +
          "The app has prepared an updated keymap. It uses the unused keys F13–F24 as signals; " +
          "on a PC running this app, other apps never see them.");

    public static string ExperimentalBadge => T("試験的", "Experimental");

    public static string FirmwareExperimental =>
        T("試験的な機能です。書き換えたキーマップがビルドできることと、作者のキーボードで動くことは確かめましたが、" +
          "ほかのキーボードではまだ確かめられていません。動いたかどうかを知らせてもらえると助かります" +
          "（「動作確認」のステップから報告できます）。",
          "This feature is experimental. Updated keymaps are known to build and to work on the author's keyboard, " +
          "but haven't been tried on other keyboards yet. Please let us know whether it worked " +
          "(you can report it from the \"Try it out\" step).");

    public static string FirmwareLeadUpdate =>
        T("このキーマップには、以前このアプリで入れた合図キーがあります。キーマップの変更に合わせて、その部分を更新します。",
          "This keymap has signal keys that this app added before. They will be updated to match your latest keymap.");

    public static string FirmwareConditional(IEnumerable<int> ifLayers) =>
        T($"{string.Join(" と ", ifLayers.Select(l => $"L{l}"))} を同時に押すと表示されます",
          $"Shown while {string.Join(" and ", ifLayers.Select(l => $"L{l}"))} are held together");

    public static string FirmwareLeadReady =>
        T("このキーマップには合図キーが入っています。キーボードに書き込み済みなら、次のステップで確かめられます。",
          "This keymap already has signal keys. If it's on your keyboard, you can try it in the next step.");

    public static string FirmwareLeadNothing =>
        T("合図を付けられるレイヤーがありません。キーボードを書き換えなくても、ショートカットで各レイヤーを表示できます。",
          "No layer can get a signal. You can still show layers with shortcuts, without changing your keyboard.");

    public static string FirmwareLeadSample =>
        T("サンプルのキーマップは書き換えません。自分のキーマップを選んだあと、ここに戻ってきてください" +
          "（トレイの「初期設定をやり直す…」）。",
          "The sample keymap is not changed. Come back here after choosing your own keymap (\"Run setup again…\" in the tray).");

    public static string FirmwareHasSignal(string key) => T($"合図キーあり（{key}）", $"Has a signal key ({key})");
    public static string FirmwareWillAdd(string key) => T($"{key} を合図にします", $"Will use {key}");

    public static string SkipReasonText(ZmkOverlay.Core.Zmk.SkippedLayer skipped) => skipped.Reason switch
    {
        ZmkOverlay.Core.Zmk.SkipReason.ToggleOrOneShot =>
            T("&tog / &to / &sl で入るので、合図を付けられません", "Entered with &tog / &to / &sl, so it can't get a signal"),
        ZmkOverlay.Core.Zmk.SkipReason.CustomHoldTap =>
            T($"自作の {skipped.Detail} で入るので、書き換えません", $"Entered with your own {skipped.Detail}, so it is left as is"),
        ZmkOverlay.Core.Zmk.SkipReason.NoFreeSignalKey =>
            T("合図に使える F13〜F24 が足りません", "No free F13–F24 key is left for a signal"),
        _ =>
            T("キーで入るレイヤーではないので、追従できません（ショートカットで表示できます）",
              "Not entered with a key, so it can't be followed (you can show it with a shortcut)"),
    };

    public static string FirmwareBackup =>
        T("先に、今のファームを保存しておいてください。うまくいかなくても書き戻せます" +
          "（「ビルド状況（Actions）を開く」→ 緑のチェックが付いた最新のビルド → 下の「Artifacts」からダウンロード）。",
          "First, save your current firmware so you can go back if needed " +
          "(\"Open builds (Actions)\" → the latest build with a green check → download it from \"Artifacts\").");

    public static string FirmwareGitHubSteps =>
        T("手順:\n" +
          "  1. 下のボタンを押すと、書き換えた内容がコピーされ、GitHub の編集画面が開きます\n" +
          "  2. 編集欄をクリックして Ctrl+A（全部選択）→ Ctrl+V（貼り付け）\n" +
          "  3. 右上の「Commit changes」を押し、そのまま確定します\n" +
          "  4. ビルド状況で緑のチェックが付くまで待ち（5〜10 分）、できたファームをキーボードに書き込みます\n" +
          "  5. 「取り直して確かめる」を押します\n" +
          "赤い × になったら書き込まず、「取り直して確かめる」を押してから、下に出る「うまくいかなかったとき」に進みます。",
          "Steps:\n" +
          "  1. The button below copies the updated keymap and opens GitHub's editor\n" +
          "  2. Click in the editor, then press Ctrl+A (select all) and Ctrl+V (paste)\n" +
          "  3. Press \"Commit changes\" at the top right and confirm\n" +
          "  4. Wait for a green check in the builds (5–10 min), then flash the firmware to your keyboard\n" +
          "  5. Press \"Fetch again and check\"\n" +
          "If the build gets a red cross, don't flash it. Press \"Fetch again and check\", " +
          "then go to \"If something goes wrong\" that appears below.");

    public static string CopyAndOpenEditor => T("コピーして GitHub の編集画面を開く", "Copy and open GitHub's editor");
    public static string OpenActions => T("ビルド状況（Actions）を開く", "Open builds (Actions)");
    public static string RecheckGitHub => T("取り直して確かめる", "Fetch again and check");

    public static string CopiedAndOpened =>
        T("書き換えた内容をコピーしました。開いた画面に貼り付けてください。", "Copied. Paste it into the page that opened.");

    public static string FirmwareLocalSteps =>
        T("手順:\n" +
          "  1. 今のファームを保存しておきます（うまくいかなくても書き戻せます）\n" +
          "  2. 下のボタンでキーマップを書き換えます（元のファイルは .bak として残します）\n" +
          "  3. いつもの方法で zmk-config をビルドし、できたファームをキーボードに書き込みます\n" +
          "ビルドが失敗したら書き込まず、下に出る「うまくいかなかったとき」に進みます。",
          "Steps:\n" +
          "  1. Save your current firmware, so you can go back if needed\n" +
          "  2. Update the keymap with the button below (the original is kept as .bak)\n" +
          "  3. Build your zmk-config as usual and flash the firmware to your keyboard\n" +
          "If the build fails, don't flash anything. Go to \"If something goes wrong\" that appears below.");

    public static string SaveOverwrite => T("キーマップを書き換える", "Update the keymap file");
    public static string SaveAs => T("別の名前で保存…", "Save as…");

    public static string ConfirmOverwrite(string path) =>
        T($"{path} を書き換えます。元のファイルは .bak として同じフォルダに残します。よろしいですか？",
          $"{path} will be updated. The original is kept as .bak in the same folder. Continue?");

    public static string KeymapChangedMeanwhile =>
        T("キーマップのファイルが変わっていたので、書き換えを作り直しました。内容を確かめて、もう一度押してください。",
          "The keymap file had changed, so the update was prepared again. Check it and press the button again.");

    public static string Saved(string backup) => T($"書き換えました。元のファイルは {backup} にあります。", $"Updated. The original is at {backup}.");

    public static string SavedAs(string path) =>
        T($"{path} に保存しました。zmk-config のキーマップと差し替えてビルドしてください。",
          $"Saved to {path}. Replace your zmk-config keymap with it and build.");

    public static string ShowChanges => T("変更点を見る", "Show changes");
    public static string ChangedLinesHeader => T("書き換える行:", "Lines that change:");
    public static string AddedBlockHeader => T("追加する定義:", "Definitions that are added:");

    public static string FirmwareFlashNote =>
        T("書き込み方はキーボードによって違います（分割キーボードでは、キーマップを持つ側だけでよいことが多い）。" +
          "キーボードの説明に従ってください。",
          "How to flash depends on your keyboard (for split keyboards, usually only the half that holds the keymap). " +
          "Follow your keyboard's instructions.");

    public static string FirmwareUndoTitle => T("うまくいかなかったとき", "If something goes wrong");

    public static string FirmwareUndoGitHub =>
        T("ビルドが赤い × になったら、ファームは書き込まないでください。下のボタンで、書き換えを取り除いたキーマップをコピーして " +
          "GitHub の編集画面を開きます。貼り付けて Commit すれば元に戻ります。" +
          "書き込んだあとで具合が悪いときは、保存しておいたファームを書き戻してください。",
          "If the build gets a red cross, don't flash it. The button below copies your keymap without the changes " +
          "and opens GitHub's editor; paste it and commit to go back. " +
          "If something is wrong after flashing, flash the firmware you saved before.");

    public static string FirmwareUndoLocal =>
        T("ビルドが失敗したら、ファームは書き込まないでください。下のボタンで、キーマップから書き換えを取り除けます。" +
          "書き込んだあとで具合が悪いときは、保存しておいたファームを書き戻してください。",
          "If the build fails, don't flash it. The button below removes the changes from your keymap file. " +
          "If something is wrong after flashing, flash the firmware you saved before.");

    public static string UndoCopyAndOpen =>
        T("書き換えを取り除いてコピーし、編集画面を開く", "Copy without the changes and open GitHub's editor");

    public static string UndoLocal => T("キーマップから書き換えを取り除く", "Remove the changes from the keymap file");

    public static string ConfirmUndo(string path) =>
        T($"{path} から、このアプリが入れた書き換えを取り除きます。今のファイルは .bak として同じフォルダに残します。よろしいですか？",
          $"The changes this app made will be removed from {path}. The current file is kept as .bak in the same folder. Continue?");

    public static string Undone(string backup) =>
        T($"書き換えを取り除きました。取り除く前のファイルは {backup} にあります。",
          $"The changes were removed. The file as it was before is at {backup}.");

    public static string UndoCopied =>
        T("書き換えを取り除いた内容をコピーしました。開いた画面に貼り付けて、Commit してください。",
          "Copied without the changes. Paste it into the page that opened and commit.");

    public static string ReportProblem => T("問題を報告する（GitHub）", "Report the problem (GitHub)");
    public static string ReportResult => T("結果を報告する（GitHub）", "Report the result (GitHub)");

    public static string TestReportNote =>
        T("キーボードの書き換えは試験的な機能です。レイヤーキーを試したら、動いたかどうかを知らせてください。" +
          "ほかの人がこの機能を使うかどうか決める手がかりになります。",
          "Updating the keyboard is an experimental feature. After trying your layer keys, please let us know whether it worked. " +
          "It helps others decide whether to use it.");

    public static string TestTitle => T("動作を確かめる", "Try it out");

    public static string TestLead =>
        T("キーボードでレイヤーキーを押してみてください。合図が届いたレイヤーに ✓ が付き、オーバーレイにそのレイヤーが出ます。",
          "Press your layer keys. Layers whose signal arrives get a ✓, and the overlay shows them.");

    public static string TestNothing =>
        T("合図キーのあるレイヤーがまだありません。キーボードを書き換えて書き込んだあとで試せます。" +
          "それまでは、ショートカットやトレイのメニューからレイヤーを表示できます。",
          "No layer has a signal key yet. You can try this after updating and flashing your keyboard. " +
          "Until then, show layers with shortcuts or from the tray menu.");

    public static string DoneTitle => T("準備ができました", "You're all set");

    public static string DoneLead(string toggleHotkey) =>
        T($"オーバーレイは、画面右下のトレイにあるアイコンから操作できます（見当たらなければ、タスクバーの「^」の中にあります）。" +
          $"{toggleHotkey} で有効・無効を切り替えられます。" +
          "設定はトレイの「設定…」からいつでも変えられます。exe をもう一度開いても、設定画面が開きます。",
          $"Use the icon in the system tray (bottom right) to control the overlay. If you can't see it, it is under \"^\" on the taskbar. " +
          $"{toggleHotkey} turns it on and off. " +
          "You can change settings any time from \"Settings…\" in the tray, or by opening the app again.");
}
