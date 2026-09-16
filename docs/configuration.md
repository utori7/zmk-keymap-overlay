# 設定ファイル（config.json）

ふつうは設定画面（トレイ →「設定…」）から変えればよい。変更はその場で反映・保存される。
ファイルを手で編集したときは、トレイの「キーマップを再読み込み」で反映される。

## 置き場所

`config.json` を **exe と同じフォルダ**から探し、無ければ `%APPDATA%\ZmkOverlay\` を見る。
どちらにも無ければ初回起動として既定値を書き出し、初期設定の案内を開く。
`--config <パス>` で起動すると、そのファイルを使う。

パスは設定ファイルからの相対でも絶対でもよい。`_comment` のように、アプリが知らない項目は書き戻しても残る。

## 項目

| キー | 既定値 | 意味 |
|---|---|---|
| `language` | `auto` | 表示言語。`ja` / `en`。`auto` は Windows の表示言語に従う |
| `displayMode` | `layersOnly` | 有効のときの見せ方。`layersOnly` = L1 以上にいるあいだだけ表示 / `always` = 常に表示 |
| `keyUnitPx` | `44` | キー 1u のピクセル数。全体の大きさはこれで決まる |
| `opacity` | `0.88` | オーバーレイの不透明度 |
| `position` | `BottomCenter` | `BottomCenter` / `BottomLeft` / `BottomRight` / `TopCenter` / `TopLeft` / `TopRight` / `Center` |
| `margin` | `48` | 画面端からの余白 |
| `keyboardLayout` | `jis` | PC のキーボード配列（`jis` / `us`）。同じキーでも出る記号が変わる |
| `toggleHotkey` | `Ctrl+Alt+K` | 有効 / 無効の切り替え。`{"modifiers": ["Ctrl","Alt"], "key": "K"}` |
| `enableManualLayerKeys` | `true` | ショートカットでレイヤーを手で表示する |
| `layerHotkeys` | なし | レイヤー番号 → ショートカット。例 `{"1": {"modifiers": ["Ctrl","Shift"], "key": "Q"}}`。書かなければ `Ctrl+Alt+<番号>`（0〜9）、`key` を空にすると割り当てない |
| `layerSync.enabled` | `true` | キーボードのレイヤーに追従する |
| `layerSync.mode` | `hold` | `hold` = 押しているあいだだけ表示 / `toggle` = 押すたびに切り替え |
| `layerSync.pollIntervalMs` | `15` | 合図キーが離されたかを見に行く間隔 |
| `layerSync.graceMs` | `150` | 押下を観測できないまま経過したら離されたとみなす時間 |
| `layoutFile` | `data/layouts/pyuron.json` | 手書き JSON の物理レイアウト（ZMK のソースを使わないとき） |
| `keymapFile` | `data/keymaps/pyuron.json` | 手書き JSON のキーマップ（同上） |

### ZMK のソースを読む（`zmk`）

`zmk.keymapFile` を指定すると、`.keymap` を毎回読んで表示する。手書き JSON より優先される。
設定例は [../data/config.zmk.example.json](../data/config.zmk.example.json)。

| キー | 意味 |
|---|---|
| `zmk.keymapFile` | `.keymap` ファイル |
| `zmk.physicalLayoutFile` | 物理レイアウトを持つ `.dtsi`。省略すると、キーマップのフォルダ以下から探し、無ければキーマップの書き方から推定する |
| `zmk.source` | `local`（既定）または `github`。設定画面の「GitHub から読み込む」で取得すると `github` になる |
| `zmk.github` | GitHub から読むときの取得元（`repository` / `branch` / `keymapPath`）。取り直しに使う |
| `zmk.labelOverrides` | キーコードの表示差し替え。例 `{"INT4": "かな"}` |
| `zmk.layerNames` | レイヤー名の差し替え。既定はノード名から作る（`default_layer` → `DEFAULT`） |
| `zmk.signalKeys` | レイヤー番号 → 合図キー。キーマップから自動で読み取るので、普通は書かなくてよい。書くとそちらが優先され、空文字 `""` にすると追従しない |

読めるもの:

- `#define`（オブジェクト形・関数形、多段展開）とローカルの `#include "..."`
- `LS()` / `LC()` / `LA()` / `LG()` の入れ子
- キーマップ内で定義した hold-tap（`&hml` などの hold / tap の判別）とマクロ
- `combos`
- JIS / US の差（`LS(N8)` は US なら `*`、JIS なら `(`）

読まないもの:

- `#include <...>` のシステムヘッダ。ZMK のソースが手元に無くても動くよう、キーコード名は内蔵表で解決する
- `#if` 系の条件分岐。本文はそのまま残し、警告として通知する

解釈できなかったキーコードやビヘイビアは、**名前をそのまま表示する**。
空白にすると「キーが無い」のか「解釈できなかった」のか画面から区別できないため。

## 表示の考え方

**有効 / 無効（`Ctrl+Alt+K`）はマスタースイッチ**で、無効にしているあいだは何をしても表示されない。
有効にしているあいだの見せ方を `displayMode` で選ぶ。

| | 無効のとき | 有効のとき |
|---|---|---|
| `layersOnly`（既定） | 出ない | **L1 以上にいるあいだだけ表示**。L0 では出ない |
| `always` | 出ない | 常時表示。レイヤーに応じて中身が変わり、離すと L0 に戻る |

L0（ベースレイヤー）が自動で出ないのは、合図キーを持たないため。
