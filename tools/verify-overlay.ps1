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
#   powershell -File tools\verify-overlay.ps1 [-Configuration Debug|Release]
#
# Any running ZmkOverlay is stopped first: only one instance can run per session.
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

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

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
$app   = Join-Path $root "src\ZmkOverlay.App\bin\$Configuration\net10.0-windows\ZmkOverlay.exe"
$probe = Join-Path $root "tools\HotkeyProbe\bin\$Configuration\net10.0-windows\HotkeyProbe.exe"
$fx    = Join-Path $root 'tests\fixtures\zmk-config\config'
$cfg   = Join-Path $env:TEMP 'zmk-overlay-verify.json'
$title = 'ZMK Keymap Overlay'
$settingsTitle = 'ZMK Keymap Overlay - Settings'.Replace('-', [string][char]0x2014)

foreach ($required in @($app, $probe)) {
    if (-not (Test-Path $required)) { Write-Error "not built: $required"; exit 2 }
}

$script:pass = 0
$script:fail = 0

function Write-Config([string]$displayMode, [string]$hiddenLayers = '[0]', [string]$syncMode = 'hold') {
    $km = ($fx + '\demo40.keymap')                     -replace '\\','/'
    $pl = ($fx + '\boards\shields\demo40\demo40.dtsi') -replace '\\','/'
@"
{
  "keyUnitPx": 44,
  "language": "en",
  "keyboardLayout": "us",
  "displayMode": "$displayMode",
  "hiddenLayers": $hiddenLayers,
  "zmk": {
    "keymapFile": "$km",
    "physicalLayoutFile": "$pl",
    "signalKeys": { "1": "F13", "2": "F14" }
  },
  "layerSync": { "enabled": true, "mode": "$syncMode" }
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

function CheckHolding([string]$label, [bool]$expected, [string]$key = 'F13') {
    Start-Process $probe -ArgumentList '--hold',$key,'2500' -WindowStyle Hidden
    Start-Sleep -Milliseconds 1100
    $r = Check $label $expected
    Start-Sleep -Seconds 2
    return $r
}

# MOD_ALT | MOD_CONTROL | MOD_NOREPEAT
$script:CtrlAlt  = 1 -bor 2 -bor 0x4000
$script:NoRepeat = 0x4000
$script:Ctrl     = 2 -bor 0x4000

function AssertHotkeys([string]$where) {
    $missing = @()
    if (-not [Ov]::HeldBySomeoneElse($script:NoRepeat, 0x7C)) { $missing += 'F13' }
    if (-not [Ov]::HeldBySomeoneElse($script:Ctrl,     0x7C)) { $missing += 'Ctrl+F13' }
    if (-not [Ov]::HeldBySomeoneElse($script:CtrlAlt,  0x4B)) { $missing += 'Ctrl+Alt+K' }
    if (-not [Ov]::HeldBySomeoneElse($script:CtrlAlt,  0x4D)) { $missing += 'Ctrl+Alt+M' }
    if (-not [Ov]::HeldBySomeoneElse($script:CtrlAlt,  0x31)) { $missing += 'Ctrl+Alt+1' }

    if ($missing.Count -eq 0) { return }

    $script:fail++
    "    !! {0}: the app does not hold {1} -- registration failed, not a logic bug" -f $where, ($missing -join ', ')
}

function StartApp([string]$displayMode, [string]$hiddenLayers = '[0]', [string]$syncMode = 'hold') {
    Get-Process ZmkOverlay -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 600

    $stale = (Get-Process ZmkOverlay -ErrorAction SilentlyContinue | Measure-Object).Count
    if ($stale -gt 0) { Write-Error "$stale stale instance(s) still running"; exit 2 }

    Write-Config $displayMode $hiddenLayers $syncMode
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

function Test-StackedLayers {
    # Entering a second layer while the first layer key is still held, then
    # releasing only the second key, must fall back to the first layer --
    # not hide the overlay while a layer key is still down.
    $p = StartApp 'layersOnly'

    '  [layersOnly] stacked layer keys'
    Start-Process $probe -ArgumentList '--hold','F13','4500' -WindowStyle Hidden
    Start-Sleep -Milliseconds 800
    Start-Process $probe -ArgumentList '--hold','F14','800' -WindowStyle Hidden
    Start-Sleep -Milliseconds 800
    Check 'holding F13 and F14'              $true
    Start-Sleep -Milliseconds 1300
    Check 'F14 released, F13 still held'     $true
    Start-Sleep -Milliseconds 2800
    Check 'both released'                    $false

    StopApp $p
    ''
}

function Test-SelectedLayers {
    # "Show only on the layers I choose". A layer the user knows by heart must
    # stay hidden -- even when the base layer is on screen, because leaving the
    # base layer up while L1 is active would show keys that are not in effect.
    $p = StartApp 'selectedLayers' '[0, 1]'

    '  [selectedLayers, L0 and L1 unchecked]'
    Check        'ON  / L0 rest'              $false
    CheckHolding 'ON  / L1 hold (unchecked)'  $false 'F13'
    CheckHolding 'ON  / L2 hold (checked)'    $true  'F14'
    Check        'after release'              $false

    StopApp $p
    ''

    $p = StartApp 'selectedLayers' '[1]'

    '  [selectedLayers, only L1 unchecked]'
    Check        'ON  / L0 rest'              $true
    CheckHolding 'ON  / L1 hold (must hide)'  $false 'F13'
    Check        'after release (L0 back)'    $true

    # Releasing a checked layer on top of an unchecked one must hide again,
    # not fall back to showing the unchecked layer.
    Start-Process $probe -ArgumentList '--hold','F13','4500' -WindowStyle Hidden
    Start-Sleep -Milliseconds 800
    Start-Process $probe -ArgumentList '--hold','F14','800' -WindowStyle Hidden
    Start-Sleep -Milliseconds 800
    Check 'holding F13 and F14'               $true
    Start-Sleep -Milliseconds 1300
    Check 'F14 released, F13 still held'      $false
    Start-Sleep -Milliseconds 2800
    Check 'both released'                     $true

    StopApp $p
    ''

    # In toggle mode the second signal leaves the layer. That has to work for a
    # layer that was never on screen, or the overlay stays gone for good.
    $p = StartApp 'selectedLayers' '[1]' 'toggle'

    '  [selectedLayers, only L1 unchecked, toggle]'
    Check 'ON  / L0 rest'                     $true
    Tap 'F13'
    Check 'toggled into L1 (must hide)'       $false
    Tap 'F13'
    Check 'toggled out (L0 back)'             $true

    StopApp $p
    ''
}

function Test-ModifierHeld {
    # With a home-row mod, Ctrl can already be down when the layer key sends
    # F13. The app must still follow, and the signal must not leak as Ctrl+F13.
    $p = StartApp 'layersOnly'

    '  [layersOnly] modifier held while entering a layer'
    CheckHolding 'holding Ctrl+F13'  $true 'ctrl+F13'
    Check        'after release'     $false

    StopApp $p
    ''
}

function Test-SecondInstance {
    # Opening the exe again must not start a second tray app (it could not get
    # any hotkeys). It asks the running one to show its settings instead.
    $p = StartApp 'layersOnly'

    '  [layersOnly] second instance'
    Start-Process $app -ArgumentList '--config', $cfg | Out-Null
    Start-Sleep -Seconds 3

    $count = (Get-Process ZmkOverlay -ErrorAction SilentlyContinue | Measure-Object).Count
    if ($count -eq 1) { $script:pass++; $mark = 'PASS' } else { $script:fail++; $mark = 'FAIL' }
    "    {0,-32} processes={1,-3} expected=1     {2}" -f 'one process only', $count, $mark

    $shown = [Ov]::Visible($settingsTitle)
    if ($shown) { $script:pass++; $mark = 'PASS' } else { $script:fail++; $mark = 'FAIL' }
    "    {0,-32} visible={1,-5} expected=True  {2}" -f 'settings window opened', $shown, $mark

    StopApp $p
    ''
}

Test-Mode 'layersOnly'
Test-Mode 'always'
Test-SelectedLayers
Test-ManualLayer
Test-StackedLayers
Test-ModifierHeld
Test-SecondInstance

Remove-Item $cfg -ErrorAction SilentlyContinue

"total: pass=$script:pass fail=$script:fail"
if ($script:fail -gt 0) { exit 1 }
