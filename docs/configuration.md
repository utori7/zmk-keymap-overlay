# 設定ファイル（config.json）

ふつうは設定画面（トレイ →「設定…」）から変えればよい。変更はその場で反映・保存される。
ファイルを手で編集したときは、トレイの「キーマップを再読み込み」で反映される。

## 置き場所

`config.json` を **exe と同じフォルダ**から探し、無ければ `%APPDATA%\ZmkOverlay\` を見る。
どちらにも無ければ初回起動として既定値を書き出し、初期設定の案内を開く。
`--config <パス>` で起動すると、そのファイルを使う。

パスは設定ファイルからの相対でも絶対でもよい。`_comment` のように、アプリが知らない項目は書き戻しても残る。

### 読めないとき

起動できなくならないように、アプリの側で次のように扱う。

- **ファイルが壊れている**（JSON として読めない）: `config.broken-<日時>.json` に移して、初回起動としてやり直す。
  手で書いた内容は移したファイルに残っている
- **キーマップが読めない**（ファイルを移した、など）: 設定ファイルはそのままにして、サンプルを表示して起動する。
  初期設定の「キーマップの場所」が開くので、選び直す。選び直すまでは設定ファイルを書き換えない
- **サンプルの場所が古い**（exe のフォルダを移した、前の版のサンプル名、など）: いまのサンプルを指すように直して保存する

`--config` で指定したファイルは開発用なので、壊れていても移さず、起動をやめる。

## 項目

| キー | 既定値 | 意味 |
|---|---|---|
| `language` | `auto` | 表示言語。`ja` / `en`。`auto` は Windows の表示言語に従う |
| `displayMode` | `layersOnly` | 有効のときの見せ方。`layersOnly` = L1 以上にいるあいだだけ表示 / `always` = 常に表示 |
| `keyUnitPx` | `44` | キー 1u のピクセル数。全体の大きさはこれで決まる |
| `opacity` | `0.88` | オーバーレイの不透明度 |
| `position` | `BottomCenter` | `BottomCenter` / `BottomLeft` / `BottomRight` / `TopCenter` / `TopLeft` / `TopRight` / `Center` |
| `margin` | `48` | 画面端からの余白 |
| `keyboardLayout` | （下記） | PC のキーボード配列（`jis` / `us`）。同じキーでも出る記号が変わる。初回起動時に PC のキーボードに合わせて書き込む（日本語キーボードなら `jis`、それ以外は `us`）。項目が無いときは `jis` |
| `toggleHotkey` | `Ctrl+Alt+K` | 有効 / 無効の切り替え。`{"modifiers": ["Ctrl","Alt"], "key": "K"}`。初回起動時、この PC の配列で Ctrl+Alt+K が文字の入力に使われる（AltGr）なら、`Ctrl+Alt+Shift+K`、それも使われるなら `Ctrl+Alt+F12` を書き込む |
| `enableManualLayerKeys` | `true` | ショートカットでレイヤーを手で表示する |
| `layerHotkeys` | なし | レイヤー番号 → ショートカット。例 `{"1": {"modifiers": ["Ctrl","Shift"], "key": "Q"}}`。書かなければ `Ctrl+Alt+<番号>`（0〜9）。ただしその組み合わせがこの PC の配列で文字の入力に使われるなら割り当てない。`key` を空にすると割り当てない |
| `layerSync.enabled` | `true` | キーボードのレイヤーに追従する |
| `layerSync.mode` | `hold` | `hold` = 押しているあいだだけ表示 / `toggle` = 押すたびに切り替え |
| `layerSync.pollIntervalMs` | `15` | 合図キーが離されたかを見に行く間隔 |
| `layerSync.graceMs` | `150` | 押下を観測できないまま経過したら離されたとみなす時間 |
| `layoutFile` | `data/layouts/corne.json` | 手書き JSON の物理レイアウト（ZMK のソースを使わないとき）。既定は同梱のサンプル |
| `keymapFile` | `data/keymaps/corne.json` | 手書き JSON のキーマップ（同上） |

文字の入力に使われる組み合わせ（Shift+英字、AltGr で記号を打つ配列の Ctrl+Alt+数字 など）は、
設定ファイルに書いてあっても登録しない。押さえるとその文字が打てなくなるため。登録しなかったことはトレイで知らせる。

### ZMK のソースを読む（`zmk`）

`zmk.keymapFile` を指定すると、`.keymap` を読んで表示する。手書き JSON より優先される。
設定例は [../data/config.zmk.example.json](../data/config.zmk.example.json)。

| キー | 意味 |
|---|---|
| `zmk.keymapFile` | `.keymap` ファイル |
| `zmk.physicalLayoutFile` | 物理レイアウトを持つ `.dtsi`。省略すると下の順で探す |
| `zmk.shieldLayoutFolder` | ZMK 本体から取ってきたシールド定義の保存先（「ZMK 本体から取得」で入る） |
| `zmk.shield` | そのシールド名（例 `corne`） |
| `zmk.source` | `local`（既定）または `github`。設定画面の「GitHub から読み込む」で取得すると `github` になる |
| `zmk.github` | GitHub から読むときの取得元（`repository` / `branch` / `keymapPath`）。取り直しに使う |
| `zmk.labelOverrides` | キーコードの表示差し替え。例 `{"INT4": "かな"}` |
| `zmk.layerNames` | レイヤー名の差し替え。既定はノード名から作る（`default_layer` → `DEFAULT`） |
| `zmk.signalKeys` | レイヤー番号 → 合図キー。キーマップから自動で読み取るので、普通は書かなくてよい。書くとそちらが優先され、空文字 `""` にすると追従しない |

物理レイアウトを探す順:

1. `zmk.physicalLayoutFile`
2. キーマップ自身
3. キーマップのフォルダ以下の `.dtsi` / `.overlay`（zmk-config のシールド定義）
4. `zmk.shieldLayoutFolder`（ZMK 本体から取ってきたもの）
5. キーマップの書き方からの推定（推定したことは警告に出る）

1 つのファイルに複数のレイアウトがあるとき（Corne の 5 列 / 6 列など）は、
`chosen` で選ばれていてキー数が合うもの、キー数が合う最初のもの、の順で選ぶ。

読めるもの:

- `#define`（オブジェクト形・関数形、多段展開）とローカルの `#include "..."`
- ZMK の共有レイアウト `#include <layouts/...>`（ZMK 本体から取ってきた保存分か、PC にある zmk のソースの中にあるとき）
- `LS()` / `LC()` / `LA()` / `LG()` の入れ子
- キーマップ内で定義した hold-tap（`&hml` などの hold / tap の判別）とマクロ
- `combos` と、条件付きレイヤー（`zmk,conditional-layers`）
- JIS / US の差（`LS(N8)` は US なら `*`、JIS なら `(`）
- `/delete-node/`・`/omit-if-no-ref/` などの devicetree の指示（読み飛ばす）

読まないもの:

- `#include <...>` のシステムヘッダ（`<layouts/...>` を除く）。ZMK のソースが手元に無くても動くよう、キーコード名は内蔵表で解決する
- `#if` 系の条件分岐。本文はそのまま残し、警告として通知する
- zmk-helpers の `ZMK_LAYER(...)` のようなマクロで書いたキーマップ。展開できないので、その旨を伝えて読み込みをやめる

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

複数のレイヤーキーを同時に押しているときは、ZMK と同じく番号の大きいレイヤーを表示する。
条件付きレイヤー（Lower + Raise で Adjust など）は、その組み合わせを押しているあいだ表示する。
