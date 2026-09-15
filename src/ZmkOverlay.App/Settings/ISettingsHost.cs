using System;
using System.Collections.Generic;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;

namespace ZmkOverlay.App.Settings;

/// <summary>
/// 設定画面から見たアプリ。
///
/// 設定画面は <see cref="Config"/> の複製を書き換えて <see cref="TryApply"/> に渡すだけで、
/// ホットキーの登録やオーバーレイの作り直しは知らない。反映の手順はアプリ側の 1 か所にしかない。
/// </summary>
internal interface ISettingsHost
{
    /// <summary>いま効いている設定。書き換えずに <see cref="AppConfig.Clone"/> してから使うこと。</summary>
    AppConfig Config { get; }

    string ConfigPath { get; }

    PhysicalLayout Layout { get; }

    Keymap Keymap { get; }

    IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// キーマップを読めたら保存して反映する。読めなければ何も変えず、理由を返す。
    /// 読めない設定を保存すると、次回起動できなくなるため。
    /// </summary>
    bool TryApply(AppConfig config, out string? error);

    /// <summary>スタートアップフォルダの実体を見る。設定ファイルには持たない。</summary>
    bool RunAtLogin { get; }

    bool TrySetRunAtLogin(bool enable, out string? error);

    /// <summary>
    /// ショートカットを入力しているあいだ、登録済みのホットキーを外す。
    /// 外さないと、いまの組み合わせを押しても入力欄に届かない（OS に横取りされる）。
    /// </summary>
    IDisposable SuspendHotkeys();

    /// <summary>反映が終わった。表示中の値やプレビューを取り直す合図。</summary>
    event Action? Applied;

    /// <summary>合図キーを受け取った（レイヤー番号）。</summary>
    event Action<int>? SignalReceived;
}
