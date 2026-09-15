# Generates src/ZmkOverlay.App/Assets/app.ico.
#
# The icon is drawn from code so it can be changed without an image editor
# and the result is reproducible. Edit the drawing in Draw-Icon, then run:
#
#   powershell -File tools\make-icon.ps1
#
# ASCII only on purpose. PowerShell 5.1 reads a BOM-less .ps1 as ANSI.
#
# Frames below 256 px are stored as 32-bit DIBs and the 256 px frame as PNG.
# System.Drawing.Icon, which the tray icon goes through, does not reliably
# read PNG-compressed frames at small sizes.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root  = Split-Path $PSScriptRoot -Parent
$out   = Join-Path $root 'src\ZmkOverlay.App\Assets\app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Blue tile, the same accent as the active layer tab on the overlay.
    $pad  = [Math]::Max(0.5, $s * 0.04)
    $tile = New-RoundedPath $pad $pad ($s - 2 * $pad) ($s - 2 * $pad) ($s * 0.22)
    $blue = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 63, 107, 201))
    $g.FillPath($blue, $tile)

    # Four keys. The top-left one is amber: the layer key being held.
    $margin = $s * 0.21
    $gap    = [Math]::Max(1, $s * 0.08)
    $key    = ($s - 2 * $margin - $gap) / 2
    $radius = [Math]::Max(0.5, $key * 0.22)
    $white  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(240, 255, 255, 255))
    $amber  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 240, 179, 91))

    for ($row = 0; $row -lt 2; $row++) {
        for ($col = 0; $col -lt 2; $col++) {
            $x = $margin + $col * ($key + $gap)
            $y = $margin + $row * ($key + $gap)
            if ($row -eq 0 -and $col -eq 0) { $brush = $amber } else { $brush = $white }
            $g.FillPath($brush, (New-RoundedPath $x $y $key $key $radius))
        }
    }

    $g.Dispose()
    return $bmp
}

function Get-DibBytes($bmp) {
    $s  = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w  = New-Object System.IO.BinaryWriter($ms)

    $maskRow = [int]([Math]::Floor(($s + 31) / 32) * 4)

    # BITMAPINFOHEADER. The height is doubled because the AND mask follows the pixels.
    $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2))
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0)
    $w.Write([int]($s * $s * 4 + $maskRow * $s))
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)

    # Pixels, bottom-up, straight (not premultiplied) BGRA.
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }

    # AND mask. All zero: the alpha channel already carries transparency.
    $w.Write((New-Object byte[] ($maskRow * $s)))

    $w.Flush()
    return ,$ms.ToArray()
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

$frames = New-Object System.Collections.Generic.List[object]
foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    if ($s -ge 256) { $bytes = Get-PngBytes $bmp } else { $bytes = Get-DibBytes $bmp }
    $bmp.Dispose()
    $frames.Add([pscustomobject]@{ Size = $s; Bytes = [byte[]]$bytes })
}

New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$fs = [System.IO.File]::Create($out)
$w  = New-Object System.IO.BinaryWriter($fs)

# ICONDIR, then one ICONDIRENTRY per frame, then the frame data.
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$frames.Count)

$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    if ($f.Size -ge 256) { $dim = 0 } else { $dim = $f.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$f.Bytes.Length); $w.Write([int]$offset)
    $offset += $f.Bytes.Length
}

foreach ($f in $frames) { $w.Write($f.Bytes) }
$w.Dispose()

"wrote $out ($($frames.Count) frames)"
