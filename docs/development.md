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
tools/sample/             同梱サンプルの元になるキーマップ（data/ はここから作る）
tests/                    パーサ・生成・取得のテストと、フィクスチャ
  fixtures/zmk-config/    このリポジトリのために書いた 40 キーの分割キーボード（demo40）の zmk-config
  fixtures/zmk/           ZMK 本体の Corne 一式（MIT、ZMK と同じ並び）
data/                     サンプル（Corne）のレイアウトとキーマップ。ビルド時に出力先へコピーされる
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

同じ利用者のセッションでは 1 つしか常駐しない。すでに動いていると、2 つ目は 1 つ目の設定画面を開かせて終わる
（`--config` を付けていても同じ）。開発版を試すときは、動いているものをトレイから終了しておく。
`--render*` と `--startup` は常駐しないので、動いているものがあってもそのまま使える。

## 画面を書き出す

常駐させずに PNG へ書き出せる。画面の外で描くので、デスクトップには何も出ない。

```bash
dotnet run --project src/ZmkOverlay.App -- --render out            # オーバーレイの全レイヤー
dotnet run --project src/ZmkOverlay.App -- --render-settings out   # 設定画面の全ページ
dotnet run --project src/ZmkOverlay.App -- --render-setup out      # 初期設定の全ステップ
```

**ビルドに失敗したまま実行しないこと。** 古い exe はこの指定を知らず、普通に常駐して戻ってこない。

### README の画像を撮り直す

`docs/images/settings-display-{ja,en}.png` は「表示」のページを書き出して切り抜いたもの。
利用者のキーマップやパスが写らないよう、テスト用の demo40 を読ませる。次の設定を
どこかに保存して（パスはその置き場所からの相対）、`language` だけ `ja` / `en` に変えて 2 回書き出す。

```json
{
  "language": "ja",
  "keyUnitPx": 44,
  "opacity": 0.88,
  "position": "BottomCenter",
  "margin": 48,
  "keyboardLayout": "jis",
  "displayMode": "selectedLayers",
  "hiddenLayers": [0, 1],
  "zmk": {
    "keymapFile": "tests/fixtures/zmk-config/config/demo40.keymap",
    "physicalLayoutFile": "tests/fixtures/zmk-config/config/boards/shields/demo40/demo40.dtsi",
    "signalKeys": { "1": "F13", "2": "F14", "3": "F15", "4": "F16", "5": "F17" }
  },
  "toggleHotkey": { "modifiers": ["Ctrl", "Alt"], "key": "K" },
  "clickThroughHotkey": { "modifiers": ["Ctrl", "Alt"], "key": "M" },
  "layerSync": { "enabled": true, "mode": "hold" }
}
```

- **合図キーは全レイヤーに入れる。** 入れないとタブが点線になり、チェックにも「（合図キーなし）」が付く。
  「いまの状態」も「追従できるレイヤーがありません」になる
- **`keyboardLayout` は英語版も `jis`。** demo40 は JIS 専用のキー（`INT3` / `INT4` / `INT5`）を使うので、
  `us` にすると ¥ や 変換 ではなく生のキーコード名が出る
- **ショートカットの欄も書く。** 「表示」には有効 / 無効とオーバーレイの操作の 2 つが出る。
  書かないと初回起動時に選び直した組み合わせが写り、撮るたびに絵が変わる
- **切り抜きは要らないことが多い。** 書き出しはページの中身が全部入る高さになる
  （`OffscreenRenderer.RenderWholePage`。ウィンドウをいくら高くしても Windows が作業領域の高さで抑えるので、
  中身を測り直している）。ウィンドウより短いページだけ、下に余白が残るので切る。
  切るときは幅そのまま、高さは中身の最後の行 + 20px
- 選択色は Windows のアクセントカラーがそのまま出る。撮り直すと前の画像と色が変わることがある

## テスト

```bash
dotnet test
```

パーサは zmk-config 一式（`tests/fixtures/`）に対しても走らせている。
合成した小さな入力だけでは、HRM・入れ子修飾・JIS 別名・複数の物理レイアウトといった実際の組み合わせを踏めないため。
fixtures には、このリポジトリのために書いた demo40 と、ZMK 本体の Corne（MIT）だけを置く。
ライセンスの分からない他人の zmk-config は入れない。

表示の出し入れの規則は、ホットキー・レイヤー追従・ウィンドウの可視状態が噛み合った結果なので
単体テストで囲えない。実アプリを合成キーで動かして確かめる（約 2 分半、25 項目）。

```bash
powershell -File tools/verify-overlay.ps1
powershell -File tools/verify-overlay.ps1 -Configuration Release
```

- 実行中はキーボードに触らない。起動中のオーバーレイは終了させられる（常駐は 1 つだけなので）
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
dotnet run --project tools/KeymapDump -- tests/fixtures/zmk-config/config/demo40.keymap
```

`config.json` を渡すと、アプリと同じ設定で読んだ結果が出る。

```bash
dotnet run --project tools/KeymapDump -- path/to/config.json
```

## 同梱サンプルを作り直す

`data/` の JSON は手で書かない。`tools/sample/corne-demo.keymap` と ZMK 本体の Corne のレイアウトから作る。
表示言語や PC の配列で変わらないよう、英語・US 配列のラベルで固定される。

```bash
dotnet run --project tools/KeymapDump -- --json tools/sample/corne-demo.keymap tests/fixtures/zmk/app/boards/shields/corne/corne.dtsi data/layouts/corne.json data/keymaps/corne.json "Generated by tools/KeymapDump --json from tools/sample/corne-demo.keymap (MIT) and ZMK's 6-column Corne layout (MIT, The ZMK Contributors). Do not edit by hand."
```

## ファームを焼く前に動作を見る

`HotkeyProbe --hold` で合図キーを合成できる。オーバーレイを起動した状態で:

```bash
dotnet run --project tools/HotkeyProbe -- --hold F13 3000
```

3 秒間 L1 が表示され、離すと消えれば PC 側は正しく動いている。
修飾キーを押したままの状況は `--hold ctrl+F13 3000` で作れる。

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
