# ZMK Keymap Overlay

ZMK のキーマップを Windows の画面上に半透過で重ねて表示する常駐ツール。

設計と技術的な判断の理由は [DESIGN.md](DESIGN.md) にある。

## 現状

**Phase 0 / 1 / 2 が完了。**

- 半透過・最前面・クリックスルー・フォーカスを奪わないオーバーレイ
- `Ctrl+Alt+K` で有効 / 無効（無効のあいだは何をしても表示されない）
- **キーボードのレイヤーに追従**（既定では L1 以上にいるあいだだけ表示）
- **`.keymap` と `.dtsi` を直接読む**（手書き JSON も引き続き使える）
- `Ctrl+Alt+<レイヤー番号>` で手動表示
- Windows のサインイン時に自動起動（任意・レジストリは使わない）
- タスクトレイから 有効切替 / 見せ方 / 自動起動 / 再読み込み / 終了

レイヤー追従を使うには ZMK 側にも変更が要る。手順は [zmk/README.md](zmk/README.md)。
**ファームを焼く前でも PC 側は普通に動く**（合図キーが来ないだけ）。

まだ実装していないもの: 押下キーのハイライト、設定 UI。
ハイライトは全キー入力の観測が要るため、このツールの前提と衝突する。
選択肢と比較は [DESIGN.md](DESIGN.md) の「押下キーのハイライト」を参照。

## 自動起動

トレイメニューの **「Windows のサインイン時に起動する」** で切り替える。
スタートアップフォルダにショートカットを置くだけで、レジストリは触らない。
エクスプローラで `shell:startup` を開けば実体が見えるし、消せば解除される。

コマンドからも操作できる。

```bash
ZmkOverlay.exe --startup on
ZmkOverlay.exe --startup off
ZmkOverlay.exe --startup status
```

`--config` を付けて起動していれば、その指定もショートカットに引き継がれる。

## 動かす

開発には .NET 10 SDK が要る。

```bash
dotnet run --project src/ZmkOverlay.App
```

起動するとタスクトレイに常駐する。ウィンドウは `Ctrl+Alt+K` を押すまで出ない。

### 見た目だけ確認する

常駐させずに全レイヤーを PNG に書き出せる。

```bash
dotnet run --project src/ZmkOverlay.App -- --render out
```

## 配布する

会社 PC のように管理者権限が使えない環境を前提にしている。
どちらの形式もインストーラ不要・レジストリ不要で、フォルダごと置けば動く。

**ランタイム同梱（exe 1 個・約 150MB・.NET 不要）**

```bash
dotnet publish src/ZmkOverlay.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

**ランタイム別（数 MB・.NET デスクトップランタイムが必要）**

```bash
dotnet publish src/ZmkOverlay.App -c Release -r win-x64 --self-contained false
```

WPF は `PublishTrimmed` に対応しないため、同梱版のサイズは削れない。

## 設定

`config.json` を **exe と同じフォルダ**から探し、無ければ `%APPDATA%\ZmkOverlay\` を見る。
どちらにも無ければ初回起動時に既定値を書き出す。

| キー | 既定値 | 意味 |
|---|---|---|
| `zmk.keymapFile` | なし | `.keymap` を直接読む。指定するとこちらが優先される |
| `zmk.physicalLayoutFile` | なし | 物理レイアウトを持つ `.dtsi` |
| `layoutFile` | `data/layouts/pyuron.json` | 手書き物理レイアウト。config.json からの相対パス |
| `keymapFile` | `data/keymaps/pyuron.json` | 手書きキーマップ |
| `keyUnitPx` | `56` | キー 1u のピクセル数。全体の大きさはこれで決まる |
| `opacity` | `0.88` | オーバーレイの不透明度 |
| `position` | `BottomCenter` | `BottomCenter` / `Center` / `TopCenter` |
| `margin` | `48` | 画面端からの余白 |
| `keyboardLayout` | `jis` | ラベル解決に使う配列（Phase 1 以降） |
| `language` | `auto` | 表示言語。`ja` / `en`。`auto` は Windows の表示言語に従う |
| `toggleHotkey` | `Ctrl+Alt+K` | 表示切替 |
| `enableManualLayerKeys` | `true` | 表示中に `Ctrl+Alt+<番号>` でレイヤー切替 |
| `displayMode` | `layersOnly` | **有効のときの見せ方。下記** |
| `layerSync.enabled` | `true` | キーボードのレイヤーに追従する |
| `layerSync.mode` | `hold` | `hold` = 押している間だけ表示 / `toggle` = 押すたび切替 |
| `layerSync.pollIntervalMs` | `15` | 合図キーが離されたかを見に行く間隔 |
| `layerSync.graceMs` | `150` | 押下を観測できないまま経過したら離されたとみなす時間 |

### 表示の考え方

**`Ctrl+Alt+K` はマスタースイッチ**で、無効にしているあいだは何をしても表示されない。
有効にしているあいだの見せ方を `displayMode` で選ぶ。

| | 無効のとき | 有効のとき |
|---|---|---|
| `layersOnly`（既定） | 出ない | **L1 以上にいるあいだだけ表示**。L0 では出ない |
| `always` | 出ない | 常時表示。レイヤーに応じて中身が変わり、離すと L0 に戻る |

L0（ベースレイヤー）が自動で出ないのは、合図キーを持たないため
（[zmk/README.md](zmk/README.md) 参照）。

トレイメニューの **「表示の仕方」** からも切り替えられる。
2 つを並べてあるので、選んでいない側が何になるかも読める。
そちらで変えると設定ファイルにも書き戻される。

有効・無効はトレイメニューの先頭（「無効にする」／「有効にする」）と
アイコンのツールチップで分かる。`layersOnly` では無効にしても
画面上は何も変わらない（もともと出ていない）ため、切り替え時はバルーン通知も出す。

編集後はトレイの「設定とキーマップを再読み込み」で反映される。

### zmk-config を直接読む

`zmk.keymapFile` を指定すると、`.keymap` を毎回パースして表示する。
キーマップを変えたらトレイの「再読み込み」を押すだけでよく、
JSON を書き直す必要はない。設定例は [data/config.zmk.example.json](data/config.zmk.example.json)。

| キー | 意味 |
|---|---|
| `zmk.labelOverrides` | キーコードの表示差し替え。例 `{"INT4": "かな"}` |
| `zmk.layerNames` | レイヤー名の差し替え。既定はノード名から作る（`default_layer` → `DEFAULT`） |
| `zmk.signalKeys` | レイヤー番号 → 合図キー。例 `{"1": "F13"}` |

読めるもの:

- `#define`（オブジェクト形・関数形、多段展開）
- `LS()` / `LC()` / `LA()` / `LG()` の入れ子
- キーマップ内で定義した hold-tap（`&hml` などの hold / tap 判別）
- `combos`
- JIS / US の差（`keyboardLayout` で切替。`LS(N8)` は US なら `*`、JIS なら `(`）

読まないもの:

- `#include <...>` のシステムヘッダ。ZMK のソースが手元に無くても動くよう、
  キーコード名は内蔵表で解決する
- `#if` 系の条件分岐。本文はそのまま残し、警告として通知する

解釈できなかったキーコードやビヘイビアは**名前をそのまま表示する**。
空白にすると「キーが無い」のか「解釈できなかった」のか画面から区別できないため。

### キーマップの解釈結果をテキストで見る

どのバインディングがどう解釈されたかは、絵より一覧のほうが速い。

```bash
dotnet run --project tools/KeymapDump -- path/to/Pyuron.keymap path/to/Pyuron.dtsi
```

`config.json` を渡すと、アプリと同じ設定で読んだ結果が出る。

```bash
dotnet run --project tools/KeymapDump -- path/to/config.json
```

### ファームを焼く前に動作を見る

`HotkeyProbe --hold` で合図キーを合成できる。オーバーレイを起動した状態で:

```bash
dotnet run --project tools/HotkeyProbe -- --hold F13 3000
```

3 秒間 L1 SYM が表示され、離すと消えれば PC 側は正しく動いている。

## 構成

```
src/ZmkOverlay.Core/   UI 非依存。devicetree パーサ、モデル、設定
  Dts/                 プリプロセッサと devicetree パーサ
  Zmk/                 キーコード表、バインディング解釈、キーマップ読み取り
src/ZmkOverlay.App/    WPF。オーバーレイ、描画、Win32 相互運用、トレイ
tools/HotkeyProbe/     ホットキー方式が成立するかを測る検証ツール
tools/KeymapDump/      パーサの解釈結果をテキストで出す
tests/                 パーサのテストと、実物の zmk-config フィクスチャ
data/                  レイアウトとキーマップ。ビルド時に出力先へコピーされる
zmk/                   zmk-config へ入れる変更
```

## テスト

```bash
dotnet test
```

パーサは実物の zmk-config（`tests/fixtures/`）に対しても走らせている。
合成した小さな入力だけでは、HRM・入れ子修飾・JIS 別名といった
実際の組み合わせを踏めないため。

表示のオン・オフの規則は、ホットキー・レイヤー追従・ウィンドウの可視状態が
噛み合った結果なので単体テストで囲えない。実アプリを合成キーで動かして確かめる。

```bash
powershell -File tools/verify-overlay.ps1
```

**画面がロックされていると動かない。** ロック中は入力デスクトップが別になり、
合成キーは `GetAsyncKeyState` には映るのに `WM_HOTKEY` が配送されないため、
すべて FAIL に見える。スクリプトはロックを検出して終了する。

## HotkeyProbe

レイヤー連動の土台になる「`RegisterHotKey` で予約したキーを `GetAsyncKeyState` で
追えるか」を実測する道具。結果は [DESIGN.md](DESIGN.md) の「V1 の検証結果」にある。

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
