# Generates Assets/DesktopBadge.ico (multi-size, PNG-compressed entries).
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$outFile = Join-Path $assets 'DesktopBadge.ico'

function New-RoundedPath {
    param([float]$x, [float]$y, [float]$w, [float]$h, [float]$r)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

$colours = @(
    [System.Drawing.ColorTranslator]::FromHtml('#4C8DFF'),
    [System.Drawing.ColorTranslator]::FromHtml('#F0883E'),
    [System.Drawing.ColorTranslator]::FromHtml('#3FB950'),
    [System.Drawing.ColorTranslator]::FromHtml('#A371F7')
)

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1.0, $size * 0.03)
    $bgPath = New-RoundedPath $pad $pad ($size - 2 * $pad) ($size - 2 * $pad) ($size * 0.22)
    $bgBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#17171D'))
    $g.FillPath($bgBrush, $bgPath)

    $edge = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#3A3A46')), ([Math]::Max(1.0, $size * 0.02))
    $g.DrawPath($edge, $bgPath)

    $gap = [Math]::Max(1.0, $size * 0.075)
    $cell = ($size - 2 * $pad - 3 * $gap) / 2
    $radius = $cell * 0.28

    for ($i = 0; $i -lt 4; $i++) {
        $cx = $pad + $gap + ($i % 2) * ($cell + $gap)
        $cy = $pad + $gap + [Math]::Floor($i / 2) * ($cell + $gap)
        $cellPath = New-RoundedPath $cx $cy $cell $cell $radius
        $brush = New-Object System.Drawing.SolidBrush $colours[$i]
        $g.FillPath($brush, $cellPath)
        $brush.Dispose()
        $cellPath.Dispose()
    }

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()

    $g.Dispose()
    $bgPath.Dispose()
    $bgBrush.Dispose()
    $edge.Dispose()
    $ms.Dispose()
    $bmp.Dispose()
}

$stream = [System.IO.File]::Create($outFile)
$writer = New-Object System.IO.BinaryWriter $stream

# ICONDIR
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $png = $pngs[$i]
    $writer.Write([Byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([Byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$png.Length)
    $writer.Write([UInt32]$offset)
    $offset += $png.Length
}

foreach ($png in $pngs) { $writer.Write($png) }

$writer.Flush()
$writer.Dispose()
$stream.Dispose()

Write-Host "Wrote $outFile"
