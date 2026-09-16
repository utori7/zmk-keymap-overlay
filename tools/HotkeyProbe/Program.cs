using System.Diagnostics;
using HotkeyProbe;

// DESIGN.md の要検証項目 V1 を確かめるための道具。
//
//   「RegisterHotKey で予約したキーが GetAsyncKeyState で拾えるか」
//
// これが成り立つなら、低レベルキーボードフックを一切使わずに
// 「レイヤーキーを押している間だけオーバーレイを出す」が作れる。
// 成り立たないなら押しっぱなし表示は諦めてトグルのみになる。

const int HotkeyId = 1;
const int PollIntervalMs = 1;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var mode = args.Length > 0 ? args[0] : "--auto";
var keyName = args.Length > 1 ? args[1] : "F13";

// --tap は "ctrl+alt+K" のような組み合わせを取るので、
// 単独キーとしての解釈より先に処理する。--hold も "ctrl+F13" のように修飾キーを付けられる
// （ホームロウモッドの Ctrl を押したままレイヤーに入った状況を作るため）。
if (mode == "--tap") return RunTap(keyName);
if (mode == "--hold" && keyName.Contains('+')) return RunHoldCombo(keyName, args.Length > 2 ? args[2] : "2000");

if (!TryParseVk(keyName, out var vk))
{
    Console.Error.WriteLine($"キー '{keyName}' を解釈できません。F13 / F10 / A / 1 のように指定してください。");
    return 1;
}

Console.WriteLine($"対象キー: {keyName} (VK 0x{vk:X2})");
Console.WriteLine();

switch (mode)
{
    case "--auto":
        return RunAuto(vk, keyName);
    case "--manual":
        return RunManual(vk, keyName);
    case "--suppress":
        return RunSuppression(vk, keyName);
    case "--hold":
        return RunHold(vk, keyName, args.Length > 2 ? args[2] : "2000");
    default:
        Console.Error.WriteLine(
            "使い方: HotkeyProbe [--auto|--manual|--suppress] [キー名]\n" +
            "        HotkeyProbe --hold [キー名 | ctrl+F13] [ミリ秒]\n" +
            "        HotkeyProbe --tap  ctrl+alt+K");
        return 1;
}

// ---------------------------------------------------------------------------

int RunAuto(int vk, string keyName)
{
    Console.WriteLine("SendInput でキーを合成して測定します。");
    Console.WriteLine("実機のキーとは経路が完全には同じでないので、最後に --manual でも確かめてください。");
    Console.WriteLine();

    var control = Probe(vk, keyName, register: false);
    var registered = Probe(vk, keyName, register: true);

    if (registered is null) return 1;

    Console.WriteLine();
    Console.WriteLine("=== 結果 ===");
    Console.WriteLine();
    Report("登録なし（対照）", control!);
    Console.WriteLine();
    Report("RegisterHotKey で登録", registered);
    Console.WriteLine();

    Verdict(registered);
    return 0;
}

int RunManual(int vk, string keyName)
{
    if (!Win32.RegisterHotKey(IntPtr.Zero, HotkeyId, Win32.MOD_NOREPEAT, (uint)vk))
    {
        Console.Error.WriteLine(
            $"{keyName} を登録できませんでした。他のアプリが使っている可能性があります。");
        return 1;
    }

    try
    {
        Console.WriteLine($"{keyName} を『少し長めに押して、離す』を 3 回繰り返してください。");
        Console.WriteLine("（120 秒で打ち切ります。Ctrl+C で中断）");
        Console.WriteLine();

        var results = new List<ProbeResult>();

        for (var trial = 1; trial <= 3; trial++)
        {
            var result = WaitForRealPress(vk, TimeSpan.FromSeconds(120));
            if (result is null)
            {
                Console.WriteLine("時間切れです。");
                break;
            }

            results.Add(result);
            Console.WriteLine(
                $"  {trial} 回目: WM_HOTKEY {Fmt(result.HotkeyAt)} / " +
                $"押下検知 {Fmt(result.DownDetectedAt)} / " +
                $"離し検知 {Fmt(result.ReleaseDetectedAt)}" +
                (result.ReleaseDetectedAt is null ? "  ← 離しを検知できず" : ""));
        }

        Console.WriteLine();
        Console.WriteLine("=== 結果 ===");
        Console.WriteLine();

        if (results.Count == 0)
        {
            Console.WriteLine("1 回も検知できませんでした。");
            Console.WriteLine("キーがそもそも PC に届いていない可能性があります。");
            return 1;
        }

        var merged = new ProbeResult
        {
            Registered = true,
            HotkeyCount = results.Sum(r => r.HotkeyCount),
            HotkeyAt = results[0].HotkeyAt,
            DownDetectedAt = results.All(r => r.DownDetectedAt is not null)
                ? results[0].DownDetectedAt : null,
            ReleaseDetectedAt = results.All(r => r.ReleaseDetectedAt is not null)
                ? results[0].ReleaseDetectedAt : null,
        };

        Report($"実機の {keyName}（{results.Count} 回）", merged);
        Console.WriteLine();
        Verdict(merged);
        return 0;
    }
    finally
    {
        Win32.UnregisterHotKey(IntPtr.Zero, HotkeyId);
    }
}

/// <summary>
/// 指定したキーを指定時間だけ押しっぱなしにする。
/// ファームを書き換える前に、合図キーを受け取る側（オーバーレイ）を
/// 動かして確かめるための道具。測定はしない。
/// </summary>
int RunHold(int vk, string keyName, string durationText)
{
    if (!int.TryParse(durationText, out var ms) || ms <= 0)
    {
        Console.Error.WriteLine($"時間 '{durationText}' を解釈できません。ミリ秒で指定してください。");
        return 1;
    }

    Console.WriteLine($"{keyName} を {ms} ms 押しっぱなしにします。");

    Win32.SendKey((ushort)vk, keyUp: false);
    Thread.Sleep(ms);
    Win32.SendKey((ushort)vk, keyUp: true);

    Console.WriteLine("離しました。");
    return 0;
}

/// <summary>
/// "ctrl+alt+K" のような組み合わせを 1 回叩く。
/// オーバーレイ側のホットキーを外から叩いて確かめるための道具。
/// </summary>
int RunTap(string combo) => PressCombo(combo, holdMs: 30, verb: "叩きました");

/// <summary>修飾キーを押したまま、キーを <paramref name="durationText"/> ミリ秒押しっぱなしにする。</summary>
int RunHoldCombo(string combo, string durationText)
{
    if (!int.TryParse(durationText, out var ms) || ms <= 0)
    {
        Console.Error.WriteLine($"時間 '{durationText}' を解釈できません。ミリ秒で指定してください。");
        return 1;
    }

    return PressCombo(combo, holdMs: ms, verb: $"{ms} ms 押しました");
}

int PressCombo(string combo, int holdMs, string verb)
{
    var modifiers = new List<int>();
    int? target = null;

    foreach (var part in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        switch (part.ToLowerInvariant())
        {
            case "ctrl" or "control": modifiers.Add(0x11); break;
            case "alt": modifiers.Add(0x12); break;
            case "shift": modifiers.Add(0x10); break;
            case "win" or "windows": modifiers.Add(0x5B); break;
            default:
                if (!TryParseVk(part, out var parsed))
                {
                    Console.Error.WriteLine($"キー '{part}' を解釈できません。");
                    return 1;
                }
                target = parsed;
                break;
        }
    }

    if (target is null)
    {
        Console.Error.WriteLine("組み合わせにキーが含まれていません。例: ctrl+alt+K");
        return 1;
    }

    foreach (var modifier in modifiers) Win32.SendKey((ushort)modifier, keyUp: false);
    Thread.Sleep(20);

    Win32.SendKey((ushort)target.Value, keyUp: false);
    Thread.Sleep(holdMs);
    Win32.SendKey((ushort)target.Value, keyUp: true);

    Thread.Sleep(20);
    for (var i = modifiers.Count - 1; i >= 0; i--) Win32.SendKey((ushort)modifiers[i], keyUp: true);

    Console.WriteLine($"{combo} を{verb}。");
    return 0;
}

int RunSuppression(int vk, string keyName)
{
    Console.WriteLine("抑止の測定用に、一瞬だけウィンドウが前面に出ます。");
    Console.WriteLine();

    var probe = SuppressionProbe.Run(vk, keyName);

    if (probe.Error is not null)
    {
        Console.Error.WriteLine(probe.Error);
        return 1;
    }

    Console.WriteLine("=== 結果 ===");
    Console.WriteLine();
    Console.WriteLine($"[登録なし（対照）]");
    Console.WriteLine($"  自アプリが受け取った KeyDown : {probe.UnregisteredKeyDowns}");
    Console.WriteLine();
    Console.WriteLine($"[RegisterHotKey で登録]");
    Console.WriteLine($"  自アプリが受け取った KeyDown : {probe.RegisteredKeyDowns}");
    Console.WriteLine($"  WM_HOTKEY 受信回数           : {probe.RegisteredHotkeys}");
    Console.WriteLine();

    if (probe.UnregisteredKeyDowns == 0)
    {
        Console.WriteLine("判定: 測定できず。");
        Console.WriteLine("  登録なしでも KeyDown が届いていない。フォーカスを取れていない可能性がある。");
        return 1;
    }

    if (probe.RegisteredKeyDowns == 0 && probe.RegisteredHotkeys > 0)
    {
        Console.WriteLine("判定: 抑止される。");
        Console.WriteLine("  登録したキーはフォーカスのあるアプリに届かず、WM_HOTKEY だけが来る。");
        Console.WriteLine("  → 合図キーの検知と抑止を RegisterHotKey ひとつで賄える。");
        return 0;
    }

    Console.WriteLine("判定: 抑止されない。");
    Console.WriteLine("  登録してもキーが他アプリに届く。合図キーが漏れるので選び方を再検討すること。");
    return 0;
}

// ---------------------------------------------------------------------------

/// <summary>
/// キーを合成して押し下げ〜解放までを観測する。
/// register が true のときだけ RegisterHotKey で予約してから測る。
/// </summary>
ProbeResult? Probe(int vk, string keyName, bool register)
{
    const int HoldMs = 300;
    const int TimeoutMs = 1200;

    if (register && !Win32.RegisterHotKey(IntPtr.Zero, HotkeyId, Win32.MOD_NOREPEAT, (uint)vk))
    {
        Console.Error.WriteLine(
            $"{keyName} を登録できませんでした。他のアプリが使っている可能性があります。");
        return null;
    }

    try
    {
        DrainMessages();

        // 前のテストの解放が反映されるまで待つ。
        var settle = Stopwatch.StartNew();
        while (Win32.IsDown(vk) && settle.ElapsedMilliseconds < 500) Thread.Sleep(PollIntervalMs);

        var result = new ProbeResult { Registered = register };
        var clock = Stopwatch.StartNew();

        Win32.SendKey((ushort)vk, keyUp: false);

        var released = false;

        while (clock.ElapsedMilliseconds < TimeoutMs)
        {
            while (Win32.PeekMessage(out var msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
            {
                if (msg.message == Win32.WM_HOTKEY && msg.wParam.ToInt32() == HotkeyId)
                {
                    result.HotkeyCount++;
                    result.HotkeyAt ??= clock.Elapsed;
                }
            }

            var down = Win32.IsDown(vk);

            if (down) result.DownDetectedAt ??= clock.Elapsed;
            else if (released && result.DownDetectedAt is not null)
            {
                result.ReleaseDetectedAt ??= clock.Elapsed;
                if (result.HotkeyCount > 0 || !register) break;
            }

            if (!released && clock.ElapsedMilliseconds >= HoldMs)
            {
                released = true;
                result.ReleaseSentAt = clock.Elapsed;
                Win32.SendKey((ushort)vk, keyUp: true);
            }

            Thread.Sleep(PollIntervalMs);
        }

        if (!released) Win32.SendKey((ushort)vk, keyUp: true);

        return result;
    }
    finally
    {
        if (register) Win32.UnregisterHotKey(IntPtr.Zero, HotkeyId);
    }
}

/// <summary>実機のキー押下を待って、同じ項目を観測する。</summary>
ProbeResult? WaitForRealPress(int vk, TimeSpan timeout)
{
    DrainMessages();

    var wait = Stopwatch.StartNew();
    ProbeResult? result = null;
    Stopwatch? clock = null;

    while (wait.Elapsed < timeout)
    {
        while (Win32.PeekMessage(out var msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
        {
            if (msg.message != Win32.WM_HOTKEY || msg.wParam.ToInt32() != HotkeyId) continue;

            if (result is null)
            {
                clock = Stopwatch.StartNew();
                result = new ProbeResult { Registered = true, HotkeyAt = TimeSpan.Zero };
            }

            result.HotkeyCount++;
        }

        if (result is not null && clock is not null)
        {
            if (Win32.IsDown(vk)) result.DownDetectedAt ??= clock.Elapsed;
            else if (result.DownDetectedAt is not null)
            {
                result.ReleaseDetectedAt = clock.Elapsed;
                return result;
            }

            // 押下自体を一度も観測できないまま時間が経ったら、そこで打ち切る。
            if (clock.ElapsedMilliseconds > 3000) return result;
        }

        Thread.Sleep(PollIntervalMs);
    }

    return result;
}

void DrainMessages()
{
    while (Win32.PeekMessage(out _, IntPtr.Zero, 0, 0, Win32.PM_REMOVE)) { }
}

// ---------------------------------------------------------------------------

void Report(string title, ProbeResult r)
{
    Console.WriteLine($"[{title}]");
    Console.WriteLine($"  WM_HOTKEY 受信回数     : {r.HotkeyCount}");
    Console.WriteLine($"  WM_HOTKEY 到達         : {Fmt(r.HotkeyAt)}");
    Console.WriteLine($"  GetAsyncKeyState 押下  : {Fmt(r.DownDetectedAt)}");
    Console.WriteLine($"  GetAsyncKeyState 解放  : {Fmt(r.ReleaseDetectedAt)}");
}

string Fmt(TimeSpan? t) => t is null ? "検知できず" : $"{t.Value.TotalMilliseconds:F1} ms";

void Verdict(ProbeResult r)
{
    var canHold = r.DownDetectedAt is not null && r.ReleaseDetectedAt is not null;

    if (canHold)
    {
        Console.WriteLine("判定: V1 成立。");
        Console.WriteLine("  RegisterHotKey で予約したキーでも GetAsyncKeyState が押下・解放を返す。");
        Console.WriteLine("  → 低レベルフックなしで『押している間だけ表示』を実装できる。");
    }
    else if (r.HotkeyCount > 0)
    {
        Console.WriteLine("判定: V1 不成立。");
        Console.WriteLine("  WM_HOTKEY は届くが、GetAsyncKeyState では状態を追えない。");
        Console.WriteLine("  → 押しっぱなし表示は断念し、トグル方式にする（DESIGN.md の代案）。");
    }
    else
    {
        Console.WriteLine("判定: 測定できず。");
        Console.WriteLine("  WM_HOTKEY 自体が届いていない。キーの選択か登録の失敗を疑うこと。");
    }
}

static bool TryParseVk(string name, out int vk)
{
    vk = 0;
    name = name.Trim().ToUpperInvariant();

    if (name.Length >= 2 && name[0] == 'F' && int.TryParse(name[1..], out var n) && n is >= 1 and <= 24)
    {
        vk = 0x70 + (n - 1);   // VK_F1 = 0x70
        return true;
    }

    if (name.Length == 1 && (char.IsLetterOrDigit(name[0])))
    {
        vk = name[0];          // 'A'..'Z' / '0'..'9' はそのまま VK
        return true;
    }

    return false;
}

internal sealed class ProbeResult
{
    public bool Registered { get; init; }
    public int HotkeyCount { get; set; }
    public TimeSpan? HotkeyAt { get; set; }
    public TimeSpan? DownDetectedAt { get; set; }
    public TimeSpan? ReleaseSentAt { get; set; }
    public TimeSpan? ReleaseDetectedAt { get; set; }
}
