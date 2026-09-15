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

    public static string RunAtLoginOn => T("サインイン時に起動します", "Will start when you sign in");
    public static string RunAtLoginOff => T("サインイン時の起動をやめました", "Will no longer start when you sign in");
    public static string CannotSetRunAtLogin => T("自動起動を設定できません", "Cannot change the start-up setting");

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
    public static string CannotOpenSettings => T("設定ファイルを開けません", "Cannot open the settings file");

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
}
