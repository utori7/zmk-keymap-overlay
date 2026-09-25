# The settings file (config.json)

日本語: [configuration.ja.md](configuration.ja.md)

Normally you change these from the settings window (tray → "Settings…"). Changes take effect and are
saved straight away. If you edit the file by hand, "Reload keymap" in the tray picks it up.

## Where it lives

`config.json` is looked up **next to the exe** first, then in `%APPDATA%\ZmkOverlay\`.
If it is in neither, the app treats it as a first run: it writes the defaults and opens the setup guide.
Starting with `--config <path>` uses that file instead.

Paths may be relative to the settings file, or absolute. Keys the app does not know — such as
`_comment` — are kept when it writes the file back.

### When it cannot be read

So that the app never becomes impossible to start, it handles these cases itself:

- **The file is corrupt** (not valid JSON): it is moved to `config.broken-<date-time>.json` and the app
  starts over as a first run. Anything you wrote by hand is still in the moved file
- **The keymap cannot be read** (you moved the file, say): the settings file is left alone and the app
  starts with the sample shown. Setup opens at "Where is your keymap?" so you can choose again.
  Until you do, the settings file is not rewritten
- **The sample path is stale** (you moved the exe's folder, or it names a sample from an older version):
  it is repaired to point at the current sample and saved

A file given with `--config` is for development, so a corrupt one is not moved aside — the app stops instead.

## Keys

| Key | Default | Meaning |
|---|---|---|
| `language` | `auto` | Display language. `ja` / `en`. `auto` follows the Windows display language |
| `displayMode` | `always` | How it shows while enabled. `always` = always shown / `layersOnly` = only while you are on layer 1 or above / `selectedLayers` = only while you are on a layer that is not in `hiddenLayers`. An unknown value is treated as the default, `always` |
| `hiddenLayers` | `[0]` | Layer numbers not to show in `selectedLayers` mode, e.g. `[0, 1]`. Remove `0` to show L0 when no layer key is held. Layers not listed — including ones you add later — are shown |
| `keyUnitPx` | `44` | Pixels for one key unit (1u). This sets the overall size |
| `opacity` | `0.88` | Opacity of the overlay |
| `position` | `BottomCenter` | `BottomCenter` / `BottomLeft` / `BottomRight` / `TopCenter` / `TopLeft` / `TopRight` / `Center` |
| `margin` | `48` | Distance from the screen edge. Not used when `position` is `Center` |
| `offsetX` / `offsetY` | `0` | Distance (px) from where `position` puts it. Written when you drag the overlay. Choosing a `position` again resets it to 0 |
| `clickThrough` | `true` | Whether clicks pass through to the app underneath. Set it to `false` and the overlay takes clicks and drags instead (pick a layer with the tabs along the top, drag the overlay where you want it). Either way it never takes focus from the app you are typing in |
| `keyboardLayout` | (see below) | This PC's keyboard layout (`jis` / `us`). The same key types a different symbol depending on it. On the first run it is written to match your keyboard (`jis` for a Japanese keyboard, `us` otherwise). If the key is missing, `jis` is assumed |
| `toggleHotkey` | `Ctrl+Alt+K` | Enables / disables the overlay. `{"modifiers": ["Ctrl","Alt"], "key": "K"}`. On the first run, if Ctrl+Alt+K types a character with this PC's layout (AltGr), `Ctrl+Alt+Shift+K` is written instead, or `Ctrl+Alt+F12` if that is taken too |
| `clickThroughHotkey` | `Ctrl+Alt+M` | Switches `clickThrough`. Written like `toggleHotkey`. On the first run, if Ctrl+Alt+M types a character with this PC's layout, `Ctrl+Alt+Shift+M` is written instead, or `Ctrl+Alt+F11` if that is taken too. An empty `key` means no shortcut |
| `enableManualLayerKeys` | `true` | Show layers by hand with shortcuts |
| `layerHotkeys` | none | Layer number → shortcut, e.g. `{"1": {"modifiers": ["Ctrl","Shift"], "key": "Q"}}`. Without an entry, `Ctrl+Alt+<number>` (0–9) is used — unless that combination types a character with this PC's layout, in which case nothing is assigned. An empty `key` means no shortcut |
| `layerSync.enabled` | `true` | Follow the keyboard's layers |
| `layerSync.mode` | `hold` | `hold` = shown while the key is held / `toggle` = toggled on each press |
| `layerSync.pollIntervalMs` | `15` | How often the signal key is checked for release |
| `layerSync.graceMs` | `150` | How long to wait before assuming a press that was never observed has been released |
| `setupStep` | none | The step the setup guide opens at next. It is set when you close setup early and cleared once you reach "You're all set". While it is set, the tray menu reads "Continue setup…". You can delete it by hand |
| `layoutFile` | `data/layouts/corne.json` | Hand-written JSON physical layout, for when you are not reading ZMK sources. Defaults to the bundled sample |
| `keymapFile` | `data/keymaps/corne.json` | Hand-written JSON keymap (same) |

Combinations that type a character — Shift+letter, or Ctrl+Alt+digit on layouts where AltGr types symbols —
are not registered even if the settings file asks for them, because holding them would stop you typing that
character. The tray tells you when something was not registered.

### Reading ZMK sources (`zmk`)

Set `zmk.keymapFile` and the app reads your `.keymap` and shows it. This takes precedence over the
hand-written JSON. There is an example at [../data/config.zmk.example.json](../data/config.zmk.example.json).

| Key | Meaning |
|---|---|
| `zmk.keymapFile` | The `.keymap` file |
| `zmk.physicalLayoutFile` | A `.dtsi` holding the physical layout. Left out, it is looked up in the order below |
| `zmk.shieldLayoutFolder` | Where a shield definition downloaded from ZMK is kept (filled in by "Get from ZMK") |
| `zmk.shield` | That shield's name, e.g. `corne` |
| `zmk.source` | `local` (default) or `github`. Fetching with "Load from GitHub" in the settings window sets `github` |
| `zmk.github` | Where to fetch from (`repository` / `branch` / `keymapPath`). Used to fetch again |
| `zmk.labelOverrides` | Replace how a keycode is shown, e.g. `{"INT4": "かな"}` |
| `zmk.layerNames` | Replace a layer's name. By default it comes from the node name (`default_layer` → `DEFAULT`) |
| `zmk.signalKeys` | Layer number → signal key. These are detected in the keymap automatically, so you normally leave this out. An entry here wins, and an empty string `""` means that layer is not followed |

The physical layout is looked for in this order:

1. `zmk.physicalLayoutFile`
2. The keymap itself
3. `.dtsi` / `.overlay` files under the keymap's folder (your zmk-config's shield definition)
4. `zmk.shieldLayoutFolder` (what was downloaded from ZMK)
5. A guess from how the keymap is written (the guess is reported as a warning)

When one file holds several layouts — Corne's 5-column and 6-column, say — the app takes the one
selected with `chosen` whose key count matches, then the first one whose key count matches.

What it reads:

- `#define` (object-like and function-like, expanded repeatedly) and local `#include "..."`
- ZMK's shared layouts `#include <layouts/...>` (from what was downloaded from ZMK, or from ZMK sources on this PC)
- Nested `LS()` / `LC()` / `LA()` / `LG()`
- Hold-taps defined in the keymap (telling `&hml`'s hold from its tap, for example) and macros
- `combos` and conditional layers (`zmk,conditional-layers`)
- The JIS / US difference (`LS(N8)` is `*` on US and `(` on JIS)
- Devicetree directives such as `/delete-node/` and `/omit-if-no-ref/` (skipped over)

What it does not read:

- System headers `#include <...>` (apart from `<layouts/...>`). Keycode names are resolved from a built-in
  table, so the app works without ZMK sources on hand
- `#if` and friends. The body is left as written and reported as a warning
- Keymaps written with macros such as zmk-helpers' `ZMK_LAYER(...)`. These cannot be expanded, so the app
  says so and stops loading

Keycodes and behaviours it could not interpret are **shown by name**. Blanking them out would make
"there is no key here" and "this could not be interpreted" look the same on screen.

## How showing works

**Enable / disable (`Ctrl+Alt+K`) is the master switch**: while it is disabled, nothing is shown whatever
else you do. `displayMode` chooses how it shows while it is enabled.

| | While disabled | While enabled |
|---|---|---|
| `always` (default) | Not shown | Always shown. The contents follow the active layer, and go back to L0 when you let go |
| `layersOnly` | Not shown | **Shown only while you are on layer 1 or above.** Not shown on L0 |
| `selectedLayers` | Not shown | **Shown only while you are on a layer that is not in `hiddenLayers`.** L0 can be chosen too |

The default is `always` because sending signals from the keyboard is experimental: using the overlay as a
cheat sheet, without touching your keyboard, has to work from the moment you install it. `layersOnly` and
`selectedLayers` assume either those signals or a shortcut you press by hand.

L0 (the base layer) is not shown in `layersOnly` because it is the state of holding nothing.

`selectedLayers` exists so that layers you use often and already know stay out of your way.
If L0 is shown and you enter a layer that is not, the overlay hides — leaving L0 up would show a set of
keys different from the ones actually in effect. The layers you uncheck under
Settings → Display → "Show only on the layers I choose" are the ones that go into `hiddenLayers`.

A layer you bring up by hand, with Ctrl+Alt+digit or from the tray, is shown in every mode.

While several layer keys are held, the highest-numbered layer is shown, as in ZMK. A conditional layer
(Adjust from Lower + Raise, say) is shown while that combination is held.
