# ZMK Keymap Overlay

A small Windows tray app that shows your [ZMK](https://zmk.dev/) keyboard's keymap on the edge of the screen.
While you hold a layer key, that layer's keys appear.

![The overlay](docs/images/overlay-en.png)

[日本語](README.ja.md)

## Features

- Shows a layer only while its layer key is held (or keep it on screen all the time)
- Reads your zmk-config directly — just paste the GitHub URL, or pick a `.keymap` file on your PC
- Finds the key positions (physical layout) automatically
- Prepares an updated keymap so your keyboard can tell the PC which layer is active
- Shows any layer with a shortcut too (you can choose the combinations)
- English / Japanese, light / dark
- No administrator rights, no installer, no registry. It does **not** use a keyboard hook to watch your typing

## Requirements

- Windows 10 or 11 (64-bit)
- A keyboard running ZMK Firmware

## Install

1. Download `ZmkOverlay-<version>-win-x64.zip` from [Releases](../../releases)
2. Right-click the zip → **Extract All**, and put the folder anywhere you like (for example in Documents)
3. Double-click `ZmkOverlay.exe`
   - If Windows says **"Windows protected your PC"**, click **More info** → **Run anyway**.
     This appears because the app is not code-signed.
4. The setup guide opens. Follow it step by step

You don't need to install .NET; it is included.

![Setup: where is your keymap?](docs/images/setup-source-en.png)

## Setup guide

1. **Where is your keymap?** — a GitHub URL, a `.keymap` file on this PC, or a sample
2. **Does this look like your keyboard?** — check the preview; if the keys are misplaced, choose your shield's `.dtsi`
3. **Let your keyboard send layer signals** — see below
4. **Try it out**
5. **Done**

You can run it again any time from the tray menu (**Run setup again…**).

### Why the keyboard needs a small change

ZMK switches layers inside the keyboard, so the PC never hears about it.
The app therefore asks the keyboard to also hold one of the unused keys F13–F24 while a layer is active.
The app listens for these keys to switch the overlay, and reserves them so no other app sees them.

- The app prepares the updated keymap for you. On GitHub, you just copy it into the editor and commit
- Typing feel stays the same (it uses the same settings as ZMK's built-in `&lt`)
- **Save your current firmware first**, so you can go back if needed
- Without the change, you can still show layers with shortcuts or from the tray menu

![Setup: layer signals](docs/images/setup-firmware-en.png)

## Using it

- **Left-click the tray icon** (bottom right of the screen) for the menu
- **Ctrl+Alt+K** turns the overlay on and off
- **Ctrl+Alt+number** shows a layer (you can change these in Settings)
- **Settings** (tray → Settings…): size, position, opacity, when to show, signal keys, shortcuts,
  language, start when signing in

![Settings](docs/images/settings-display-en.png)

## Network use

The app contacts GitHub (`api.github.com` and `raw.githubusercontent.com`) only when you press
**Fetch** or **Fetch again**, or **Reload keymap** in the tray while your keymap comes from GitHub.
It never connects at startup, and it never sends your keystrokes or anything else.

## Settings file

`config.json` lives next to `ZmkOverlay.exe` (or in `%APPDATA%\ZmkOverlay\` if that folder is not writable).
You normally change everything in Settings. For the full list of keys, see
[docs/configuration.md](docs/configuration.md) (Japanese).

## Uninstall

1. If you turned on **Start when I sign in to Windows**, turn it off in Settings → General first
   (or delete the shortcut in `shell:startup`)
2. Choose **Exit** from the tray menu
3. Delete the folder. Nothing is written to the registry

## FAQ

- **A shortcut does nothing** — another app is probably using the same combination. Pick another one in Settings → Shortcuts
- **Some layers are not followed** — the setup guide's "layer signals" step tells you why
  (for example, layers entered with `&tog`)
- **The keys are in the wrong places** — in Settings → Keyboard, choose your shield's `.dtsi` as the physical layout
- **My zmk-config is private** — the app can't read private repositories. Download the files and choose the `.keymap` on your PC
- **Can I use it on a work PC?** — it needs no administrator rights or installer and uses no keyboard hook,
  but follow your company's rules

## Building from source

You need the .NET 10 SDK.

```bash
dotnet build
dotnet test
powershell -File tools/publish.ps1
```

See [docs/development.md](docs/development.md) and [DESIGN.md](DESIGN.md) (both in Japanese).

## License

[MIT](LICENSE)
