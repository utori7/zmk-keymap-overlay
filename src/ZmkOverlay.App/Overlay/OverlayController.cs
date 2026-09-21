using System;
using System.Collections.Generic;
using System.Windows;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;

namespace ZmkOverlay.App.Overlay;

/// <summary>
/// オーバーレイを出すかどうかを決める。
///
/// 状態は 2 つだけ。
///
///   有効 / 無効   ホットキーで切り替えるマスタースイッチ。
///                 無効のあいだは何が起きても出さない。
///   見せ方        有効のときに、どのレイヤーで出すか（L1 以上 / 選んだレイヤー / 常に）。
///                 <see cref="AppConfig.DisplayMode"/> で決まり、判定は <see cref="AppConfig.ShowsLayer"/>。
///
/// 「無効なのにレイヤーキーで出てくる」という状態を作らないため、
/// 表示するかどうかの判断は <see cref="AtRest"/> と
/// <see cref="BeginTransient"/> の 2 か所に集約してある。
/// </summary>
internal sealed class OverlayController
{
    private readonly OverlayWindow _window;

    private AppConfig _config;

    /// <summary>
    /// マスタースイッチ。既定で有効にしてあるのは、初回起動時に
    /// ホットキーを押すまで何も起きないのを避けるため。
    /// </summary>
    private bool _enabled = true;

    /// <summary>
    /// <c>Ctrl+Alt+&lt;番号&gt;</c> で明示的に選ばれたレイヤー。
    /// キーボードを繋いでいないときに手で見るための逃げ道。
    ///
    /// キーボードのレイヤーを実際に使った時点で忘れる。残したままにすると
    /// 「L1 以上のときだけ表示」にしているのに出しっぱなしになり、
    /// なぜそうなったのか画面から分からなくなる。
    /// </summary>
    private int? _manualLayer;

    /// <summary>
    /// 合図キーで入っているレイヤー。表示しないことにしたレイヤーでも覚えておく。
    ///
    /// トグル方式では、同じ合図キーをもう一度受け取ったらそのレイヤーを出たとみなす。
    /// ウィンドウに出ているかで判断すると、表示しないレイヤーからいつまでも出られない。
    /// </summary>
    private int? _signalLayer;

    public OverlayController(AppConfig config, PhysicalLayout layout, Keymap keymap)
    {
        _config = config;
        _window = new OverlayWindow(config, layout, keymap);
    }

    public IReadOnlyList<int> LayerIds => _window.LayerIds;

    public IReadOnlyList<(int Id, string Name)> Layers => _window.Layers;

    public IReadOnlyList<(int LayerId, string SignalKey)> SignalKeys => _window.SignalKeys;

    /// <summary>表示中のキーマップ。条件付きレイヤーの判定に使う。</summary>
    public Keymap Keymap => _window.Keymap;

    public bool IsVisible => _window.IsShown;

    public bool IsEnabled => _enabled;

    /// <summary>何も押していないときにも出す設定か。出さない設定では、有効 / 無効を切り替えても画面が変わらない。</summary>
    public bool ShowsBaseLayer => _config.ShowsLayer(BaseLayerId, BaseLayerId);

    /// <summary>
    /// 初期状態を画面に反映する。
    ///
    /// コンストラクタでやらないのは、表示が始まる前に呼び出し側が
    /// <see cref="VisibilityChanged"/> を購読し終えている必要があるため。
    /// </summary>
    public void Start() => AtRest();

    public event DependencyPropertyChangedEventHandler VisibilityChanged
    {
        add => _window.IsVisibleChanged += value;
        remove => _window.IsVisibleChanged -= value;
    }

    /// <summary>オーバーレイのタブがクリックされた（ZMK のレイヤー番号）。</summary>
    public event Action<int>? LayerTabClicked
    {
        add => _window.LayerTabClicked += value;
        remove => _window.LayerTabClicked -= value;
    }

    /// <summary>ドラッグで動かし終えた。設定の基準位置からのずれ（DIP）。</summary>
    public event Action<double, double>? Dragged
    {
        add => _window.Dragged += value;
        remove => _window.Dragged -= value;
    }

    public void Reload(AppConfig config, PhysicalLayout layout, Keymap keymap)
    {
        _config = config;
        _window.Reload(config, layout, keymap);

        if (_manualLayer is { } layerId && !LayerIds.Contains(layerId)) _manualLayer = null;

        AtRest();
    }

    /// <summary>設定だけ差し替える。トレイから見せ方を変えたときに呼ぶ。</summary>
    public void ApplyConfig(AppConfig config)
    {
        _config = config;
        AtRest();
    }

    // ---- 利用者による操作 ----

    /// <summary>マスタースイッチ。戻り値は切り替えたあとの状態。</summary>
    public bool Toggle()
    {
        _enabled = !_enabled;

        // 手で選んだレイヤーは、いったん切ったら忘れる。
        // 次に入れたときに前回の選択が残っていると理由が分からない。
        if (!_enabled) _manualLayer = null;

        AtRest();
        return _enabled;
    }

    /// <summary>レイヤーを指定して表示する。無効なら有効にしてから出す。</summary>
    public void ShowManualLayer(int layerId)
    {
        if (!LayerIds.Contains(layerId)) return;

        _enabled = true;
        _manualLayer = layerId;

        AtRest();
    }

    /// <summary>
    /// オーバーレイ上のタブをクリックした。同じレイヤーをもう一度なら、手で選んだ状態をやめて自動に戻す。
    /// </summary>
    public void ToggleManualLayer(int layerId)
    {
        if (!LayerIds.Contains(layerId)) return;

        // 解除するのは「手で選んだのが同じレイヤーだったとき」だけ。画面上で選択中に見えるかで決めてはいけない。
        // 合図キーで入っているときやベースレイヤーを出しているときも選択中に見えるので、
        // それを解除として扱うと、キーボードで入っているレイヤーのタブを押しただけで板が消える。
        if (_manualLayer == layerId)
        {
            _manualLayer = null;
            AtRest();
            return;
        }

        ShowManualLayer(layerId);
    }

    /// <summary>
    /// いま出ているレイヤーを、手で選んだことにする。出ていなければベースレイヤー。
    ///
    /// クリックを受け取る設定へ切り替えるときに呼ぶ。設定の反映は <see cref="AtRest"/> を通るので、
    /// 「L1 以上のときだけ表示」では、触れるようにした瞬間に板が消えて、触る対象が無くなってしまう。
    /// 表示を伴うので、<see cref="ShowManualLayer"/> と同じく無効なら有効にする。
    /// </summary>
    public void PinCurrentLayer()
    {
        _enabled = true;
        _manualLayer = _window.IsShown ? _window.CurrentLayerId : BaseLayerId;
    }

    // ---- キーボードのレイヤー追従 ----

    /// <summary>合図キーを受け取った。</summary>
    public void BeginTransient(int layerId)
    {
        if (!_enabled) return;

        // キーボードでレイヤーを使い始めたら、手で選んだ表示は役目を終える。
        _manualLayer = null;
        _signalLayer = layerId;

        // 表示しないレイヤーに入ったら消す。前のレイヤーを出したままにすると、
        // 実際に効いているキーと違う配置を見せることになる。
        if (!_config.ShowsLayer(layerId, BaseLayerId))
        {
            _window.HideOverlay();
            return;
        }

        if (!_window.TrySetLayer(layerId)) return;

        _window.ShowOverlay();
    }

    /// <summary>合図キーが離された。</summary>
    public void EndTransient() => AtRest();

    /// <summary>トグル方式のとき、同じレイヤーの合図キーで抜けられるようにする。表示していないレイヤーでも同じ。</summary>
    public bool IsShowingLayer(int layerId)
        => _manualLayer is null && _signalLayer == layerId;

    // ---- 表示の決定 ----

    /// <summary>
    /// レイヤーキーを押していないときに、どう見えているべきかを反映する。
    /// 表示・非表示の判断はここに集約する。
    /// </summary>
    private void AtRest()
    {
        // 合図で入ったレイヤーから出た扱いにする。解除のほか、有効 / 無効の切り替え・手で選んだとき・
        // 読み直しでもここを通る。どれも、そのあとの合図は新しく入ったものとして扱うのが自然。
        _signalLayer = null;

        if (!_enabled)
        {
            _window.HideOverlay();
            return;
        }

        if (_manualLayer is { } manual)
        {
            _window.TrySetLayer(manual);
            _window.ShowOverlay();
            return;
        }

        if (ShowsBaseLayer)
        {
            _window.TrySetLayer(BaseLayerId);
            _window.ShowOverlay();
            return;
        }

        // 何も押していないときは出さない設定。引っ込めておく。
        _window.HideOverlay();
    }

    private int BaseLayerId => LayerIds.Count > 0 ? LayerIds[0] : 0;
}
