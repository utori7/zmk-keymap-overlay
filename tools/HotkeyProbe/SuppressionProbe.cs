using System.Windows.Forms;

namespace HotkeyProbe;

/// <summary>
/// 「RegisterHotKey で予約したキーは、フォーカスのあるアプリに届かなくなる」
/// を実測する。
///
/// これが成り立つから、合図キー（F13〜）の検知と抑止が RegisterHotKey ひとつで
/// 片付く＝低レベルフックが要らない、という設計になっている。
/// 成り立たなければ F13〜が他アプリに漏れるので、合図キーの選び方を考え直すことになる。
///
/// 自分自身をフォーカスのあるアプリ役に立て、キーを合成して
/// 「WM_KEYDOWN が来るか」「WM_HOTKEY が来るか」を数える。
/// </summary>
internal sealed class SuppressionProbe : Form
{
    private const int HotkeyId = 42;
    private const int WaitMs = 250;

    private readonly int _vk;
    private readonly string _keyName;

    private int _keyDownCount;
    private int _hotkeyCount;

    public int UnregisteredKeyDowns { get; private set; }
    public int RegisteredKeyDowns { get; private set; }
    public int RegisteredHotkeys { get; private set; }
    public string? Error { get; private set; }

    private SuppressionProbe(int vk, string keyName)
    {
        _vk = vk;
        _keyName = keyName;

        Text = "HotkeyProbe";
        Width = 420;
        Height = 160;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = $"{keyName} の抑止を測定しています。\nそのままお待ちください（約 1 秒）。",
        });
    }

    /// <summary>STA スレッドで一連の測定を回し、結果を返す。</summary>
    public static SuppressionProbe Run(int vk, string keyName)
    {
        SuppressionProbe? probe = null;

        var thread = new Thread(() =>
        {
            probe = new SuppressionProbe(vk, keyName);
            Application.Run(probe);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return probe!;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // フォーカスを確実に持ってから測る。持っていないと
        // 「届かなかった」のが抑止のせいか未フォーカスのせいか区別できない。
        Activate();
        BeginInvoke(new Action(async () => await RunSequenceAsync()));
    }

    private async Task RunSequenceAsync()
    {
        // 1) 登録なし。ここで WM_KEYDOWN が来ないなら測定自体が成立していない。
        _keyDownCount = 0;
        await InjectAsync();
        UnregisteredKeyDowns = _keyDownCount;

        // 2) 登録あり。WM_KEYDOWN が来なくなれば抑止されている。
        if (!Win32.RegisterHotKey(Handle, HotkeyId, Win32.MOD_NOREPEAT, (uint)_vk))
        {
            Error = $"{_keyName} を登録できませんでした。";
            Close();
            return;
        }

        _keyDownCount = 0;
        _hotkeyCount = 0;
        await InjectAsync();
        RegisteredKeyDowns = _keyDownCount;
        RegisteredHotkeys = _hotkeyCount;

        Win32.UnregisterHotKey(Handle, HotkeyId);
        Close();
    }

    private async Task InjectAsync()
    {
        Win32.SendKey((ushort)_vk, keyUp: false);
        await Task.Delay(WaitMs);

        Win32.SendKey((ushort)_vk, keyUp: true);
        await Task.Delay(WaitMs);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId) _hotkeyCount++;
        base.WndProc(ref m);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((int)e.KeyCode == _vk) _keyDownCount++;
        base.OnKeyDown(e);
    }
}
