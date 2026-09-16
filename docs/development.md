# 開発メモ

設計と判断の理由は [DESIGN.md](../DESIGN.md)。ここは手を動かすときの手順をまとめる。

## 必要なもの

- Windows 10 / 11
- .NET 10 SDK

## 構成

```
src/ZmkOverlay.Core/      UI 非依存。devicetree パーサ、モデル、設定、書き換えの生成、GitHub からの取得
  Dts/                    プリプロセッサと devicetree パーサ
  Zmk/                    キーコード表、バインディング解釈、キーマップ読み取り、
                          物理レイアウトの推定、書き換えの生成、GitHub からの取得
  Text/                   日英の文言（Core と共通の分）
src/ZmkOverlay.App/       WPF。オーバーレイ、描画、設定画面、初期設定の案内、Win32 相互運用、トレイ
tools/HotkeyProbe/        ホットキー方式が成立するかを測る検証ツール。合成キーも送れる
tools/KeymapDump/         パーサの解釈結果をテキストで出す
tools/make-icon.ps1       アイコンを描く
tools/publish.ps1         配布 zip を作る
tools/verify-overlay.ps1  表示の出し入れの通し確認
tests/                    パーサ・生成・取得のテストと、実物の zmk-config フィクスチャ
data/                     サンプルのレイアウトとキーマップ。ビルド時に出力先へコピーされる
docs/examples/pyuron/     作者の Pyuron に手で合図キーを入れた記録
```

## 動かす

```bash
dotnet run --project src/ZmkOverlay.App
```

設定ファイルは exe の隣（`src/ZmkOverlay.App/bin/Debug/net10.0-windows/config.json`）に作られ、
初回は初期設定の案内が開く。別の設定で試すときは `--config` を付ける。
既定の探索場所を汚さずに済む。

```bash
dotnet run --project src/ZmkOverlay.App -- --config path/to/config.json
```

## 画面を書き出す

常駐させずに PNG へ書き出せる。画面の外で描くので、デスクトップには何も出ない。

```bash
dotnet run --project src/ZmkOverlay.App -- --render out            # オーバーレイの全レイヤー
dotnet run --project src/ZmkOverlay.App -- --render-settings out   # 設定画面の全ページ
dotnet run --project src/ZmkOverlay.App -- --render-setup out      # 初期設定の全ステップ
```

**ビルドに失敗したまま実行しないこと。** 古い exe はこの指定を知らず、普通に常駐して戻ってこない。

## テスト

```bash
dotnet test
```

パーサは実物の zmk-config（`tests/fixtures/`）に対しても走らせている。
合成した小さな入力だけでは、HRM・入れ子修飾・JIS 別名といった実際の組み合わせを踏めないため。

表示の出し入れの規則は、ホットキー・レイヤー追従・ウィンドウの可視状態が噛み合った結果なので
単体テストで囲えない。実アプリを合成キーで動かして確かめる（約 2 分、21 項目）。

```bash
powershell -File tools/verify-overlay.ps1
```

- 実行中はキーボードに触らない。起動中のオーバーレイは終了させられる
- **画面がロックされていると動かない。** ロック中は入力デスクトップが別になり、合成キーは
  `GetAsyncKeyState` には映るのに `WM_HOTKEY` が配送されないため、すべて FAIL に見える。
  スクリプトはロックを検出して終了する

## 配布物を作る

```bash
powershell -File tools/publish.ps1
```

`artifacts/ZmkOverlay-<バージョン>-win-x64.zip` ができる。バージョンは
`src/ZmkOverlay.App/ZmkOverlay.App.csproj` の `<Version>`。

ランタイム同梱の単一 exe なので、使う人は .NET を入れなくてよい。
WPF は `PublishTrimmed` に対応しないため、サイズは削れない。

ランタイム別（framework-dependent）でも作れるが、.NET 10 の無い PC では起動時にインストールを求められる。
公開用には使わない。

```bash
dotnet publish src/ZmkOverlay.App -c Release -r win-x64 --self-contained false
```

## 自動起動をコマンドで操作する

```bash
ZmkOverlay.exe --startup on
ZmkOverlay.exe --startup off
ZmkOverlay.exe --startup status
```

スタートアップフォルダにショートカットを置くだけで、レジストリは触らない。
`--config` を付けて起動していれば、その指定もショートカットに引き継がれる。

## キーマップの解釈結果をテキストで見る

どのバインディングがどう解釈されたかは、絵より一覧のほうが速い。

```bash
dotnet run --project tools/KeymapDump -- path/to/Pyuron.keymap path/to/Pyuron.dtsi
```

`config.json` を渡すと、アプリと同じ設定で読んだ結果が出る。

```bash
dotnet run --project tools/KeymapDump -- path/to/config.json
```

## ファームを焼く前に動作を見る

`HotkeyProbe --hold` で合図キーを合成できる。オーバーレイを起動した状態で:

```bash
dotnet run --project tools/HotkeyProbe -- --hold F13 3000
```

3 秒間 L1 が表示され、離すと消えれば PC 側は正しく動いている。

## HotkeyProbe

レイヤー連動の土台になる「`RegisterHotKey` で予約したキーを `GetAsyncKeyState` で追えるか」を
実測する道具。結果は [DESIGN.md](../DESIGN.md) の「V1 の検証結果」にある。

`SendInput` でキーを合成して自動測定する:

```bash
dotnet run --project tools/HotkeyProbe -- --auto F13
```

予約したキーが他アプリに漏れないかを測る。一瞬だけウィンドウが前面に出る:

```bash
dotnet run --project tools/HotkeyProbe -- --suppress F13
```

実際のキーボードで確かめる。指定したキーは測定中だけ他アプリに届かなくなる:

```bash
dotnet run --project tools/HotkeyProbe -- --manual F10
```

## アイコン

`tools/make-icon.ps1` がコードから描いて `src/ZmkOverlay.App/Assets/app.ico` を作る。.ico は直接編集しない。

```bash
powershell -File tools/make-icon.ps1
```
