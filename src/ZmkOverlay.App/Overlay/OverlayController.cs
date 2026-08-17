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
///   見せ方        有効のときに、常時出すか、L1 以上にいるときだけ出すか。
///                 <see cref="AppConfig.DisplayMode"/> で決まる。
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

    public OverlayController(AppConfig config, PhysicalLayout layout, Keymap keymap)
    {
        _config = config;
        _window = new OverlayWindow(config, layout, keymap);
    }

    public IReadOnlyList<int> LayerIds => _window.LayerIds;

    public IReadOnlyList<(int LayerId, string SignalKey)> SignalKeys => _window.SignalKeys;

    public bool IsVisible => _window.IsVisible;

    public bool IsEnabled => _enabled;

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

    // ---- キーボードのレイヤー追従 ----

    /// <summary>合図キーを受け取った。</summary>
    public void BeginTransient(int layerId)
    {
        if (!_enabled) return;

        // キーボードでレイヤーを使い始めたら、手で選んだ表示は役目を終える。
        _manualLayer = null;

        if (!_window.TrySetLayer(layerId)) return;

        _window.ShowOverlay();
    }

    /// <summary>合図キーが離された。</summary>
    public void EndTransient() => AtRest();

    /// <summary>トグル方式のとき、同じレイヤーの合図キーで消せるようにする。</summary>
    public bool IsShowingLayer(int layerId)
        => _window.IsVisible && _manualLayer is null && _window.CurrentLayerId == layerId;

    // ---- 表示の決定 ----

    /// <summary>
    /// レイヤーキーを押していないときに、どう見えているべきかを反映する。
    /// 表示・非表示の判断はここに集約する。
    /// </summary>
    private void AtRest()
    {
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

        if (_config.IsAlwaysVisible)
        {
            _window.TrySetLayer(BaseLayerId);
            _window.ShowOverlay();
            return;
        }

        // L1 以上にいるときだけ出す設定。素の状態では引っ込めておく。
        _window.HideOverlay();
    }

    private int BaseLayerId => LayerIds.Count > 0 ? LayerIds[0] : 0;
}
