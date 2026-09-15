# Overlay behaviour smoke test.
#
# Drives the real app with synthesised key input and checks whether the
# overlay is on screen at each point. Unit tests cannot cover this: the
# rules live in the interaction between hotkeys, the layer-sync poller and
# window visibility.
#
# ASCII only on purpose. PowerShell 5.1 reads a BOM-less .ps1 as ANSI, which
# mangles non-ASCII literals and silently corrupts the config this writes.
#
#   powershell -File tools\verify-overlay.ps1
#
# Two traps worth knowing before extending this script:
#
#   * Do not try to observe the signal key with GetAsyncKeyState from here.
#     While the app holds the key as a hotkey, another process cannot see it
#     go down -- it reads as released the whole time. The app itself can see
#     it (that is what makes hold mode work), so judge by window visibility,
#     never by key state.
#
#   * A locked workstation breaks everything silently: synthesised keys still
#     land, but WM_HOTKEY is never delivered, so every check fails for the
#     wrong reason. Guarded below.

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class Ov {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    public static bool Visible(string title) {
        bool found = false;
        EnumWindows((h, p) => {
            var t = new StringBuilder(256);
            GetWindowTextW(h, t, 256);
            if (t.ToString() == title) { found = IsWindowVisible(h); return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // A hotkey the app failed to register looks exactly like a logic bug from
    // the outside: the key does nothing. Tell the two apart by checking who
    // owns it -- if we can register it ourselves, the app does not have it.
    [DllImport("user32.dll", SetLastError=true)] static extern bool RegisterHotKey(IntPtr h,int id,uint m,uint vk);
    [DllImport("user32.dll", SetLastError=true)] static extern bool UnregisterHotKey(IntPtr h,int id);
    public static bool HeldBySomeoneElse(uint mods, uint vk) {
        if (!RegisterHotKey(IntPtr.Zero, 9990, mods, vk)) return true;
        UnregisterHotKey(IntPtr.Zero, 9990);
        return false;
    }
}
"@

# A locked workstation runs on a different input desktop. Synthesised keys
# still update the async key state, so GetAsyncKeyState looks fine, but
# WM_HOTKEY is never delivered and every check fails for the wrong reason.
if (Get-Process LogonUI -ErrorAction SilentlyContinue) {
    Write-Error 'The workstation is locked. Hotkeys are not delivered on the lock screen; unlock and run again.'
    exit 2
}

$root  = Split-Path $PSScriptRoot -Parent
$app   = Join-Path $root 'src\ZmkOverlay.App\bin\Debug\net10.0-windows\ZmkOverlay.exe'
$probe = Join-Path $root 'tools\HotkeyProbe\bin\Debug\net10.0-windows\HotkeyProbe.exe'
$fx    = Join-Path $root 'tests\fixtures\zmk-config\config'
$cfg   = Join-Path $env:TEMP 'zmk-overlay-verify.json'
$title = 'ZMK Keymap Overlay'

foreach ($required in @($app, $probe)) {
    if (-not (Test-Path $required)) { Write-Error "not built: $required"; exit 2 }
}

$script:pass = 0
$script:fail = 0

function Write-Config([string]$displayMode) {
    $km = ($fx + '\Pyuron.keymap')                     -replace '\\','/'
    $pl = ($fx + '\boards\shields\Pyuron\Pyuron.dtsi') -replace '\\','/'
@"
{
  "keyUnitPx": 44,
  "displayMode": "$displayMode",
  "zmk": {
    "keymapFile": "$km",
    "physicalLayoutFile": "$pl",
    "signalKeys": { "1": "F13", "2": "F14" }
  },
  "layerSync": { "enabled": true, "mode": "hold" }
}
"@ | Set-Content $cfg -Encoding utf8
}

function Tap([string]$combo) {
    Start-Process $probe -ArgumentList '--tap', $combo -WindowStyle Hidden -Wait
    Start-Sleep -Milliseconds 600
}

function Check([string]$label, [bool]$expected) {
    $actual = [Ov]::Visible($title)
    if ($actual -eq $expected) { $script:pass++; $mark = 'PASS' } else { $script:fail++; $mark = 'FAIL' }
    "    {0,-32} visible={1,-5} expected={2,-5} {3}" -f $label, $actual, $expected, $mark
}

function CheckHolding([string]$label, [bool]$expected) {
    Start-Process $probe -ArgumentList '--hold','F13','2500' -WindowStyle Hidden
    Start-Sleep -Milliseconds 1100
    $r = Check $label $expected
    Start-Sleep -Seconds 2
    return $r
}

# MOD_ALT | MOD_CONTROL | MOD_NOREPEAT
$script:CtrlAlt  = 1 -bor 2 -bor 0x4000
$script:NoRepeat = 0x4000

function AssertHotkeys([string]$where) {
    $missing = @()
    if (-not [Ov]::HeldBySomeoneElse($script:NoRepeat, 0x7C)) { $missing += 'F13' }
    if (-not [Ov]::HeldBySomeoneElse($script:CtrlAlt,  0x4B)) { $missing += 'Ctrl+Alt+K' }
    if (-not [Ov]::HeldBySomeoneElse($script:CtrlAlt,  0x31)) { $missing += 'Ctrl+Alt+1' }

    if ($missing.Count -eq 0) { return }

    $script:fail++
    "    !! {0}: the app does not hold {1} -- registration failed, not a logic bug" -f $where, ($missing -join ', ')
}

function StartApp([string]$displayMode) {
    Get-Process ZmkOverlay -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 600

    $stale = (Get-Process ZmkOverlay -ErrorAction SilentlyContinue | Measure-Object).Count
    if ($stale -gt 0) { Write-Error "$stale stale instance(s) still running"; exit 2 }

    Write-Config $displayMode
    $p = Start-Process $app -ArgumentList '--config', $cfg -PassThru
    Start-Sleep -Seconds 3

    AssertHotkeys "after start ($displayMode)"
    return $p
}

function StopApp($p) {
    if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force }
}

function Test-Mode([string]$displayMode) {
    $p = StartApp $displayMode
    $alwaysVisible = ($displayMode -eq 'always')

    "  [$displayMode] enabled at startup"
    Check        'ON  / L0 rest'  $alwaysVisible
    CheckHolding 'ON  / L1 hold'  $true
    Check        'ON  / L0 rest'  $alwaysVisible

    Tap 'ctrl+alt+K'
    "  [$displayMode] disabled by hotkey"
    Check        'OFF / L0 rest'  $false
    CheckHolding 'OFF / L1 hold'  $false

    Tap 'ctrl+alt+K'
    "  [$displayMode] enabled again"
    CheckHolding 'ON  / L1 hold'  $true

    StopApp $p
    ''
}

function Test-ManualLayer {
    # A layer picked with Ctrl+Alt+<n> must not outlive the next real use of
    # the keyboard's layers, or "show only on L1+" quietly becomes "always on".
    $p = StartApp 'layersOnly'

    '  [layersOnly] manual layer'
    Check 'ON  / L0 rest'             $false
    Tap 'ctrl+alt+1'
    Check 'after Ctrl+Alt+1'          $true
    AssertHotkeys 'after Ctrl+Alt+1'
    CheckHolding 'while holding F13'  $true
    Check 'after release (must hide)' $false

    Tap 'ctrl+alt+2'
    Check 'after Ctrl+Alt+2'          $true
    AssertHotkeys 'after Ctrl+Alt+2'
    Tap 'ctrl+alt+K'
    Check 'disabling clears it'       $false

    StopApp $p
    ''
}

Test-Mode 'layersOnly'
Test-Mode 'always'
Test-ManualLayer

Remove-Item $cfg -ErrorAction SilentlyContinue

"total: pass=$script:pass fail=$script:fail"
if ($script:fail -gt 0) { exit 1 }
