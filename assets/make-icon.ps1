# Generates a multi-resolution .ico from icon-source.png.
# Sizes: 16 / 24 / 32 / 48 / 64 / 128 / 256 - what Windows Explorer needs.
# Each frame is a PNG-compressed entry (Windows Vista+ supports this).

Add-Type -AssemblyName System.Drawing

$srcPath = Join-Path $PSScriptRoot "icon-source.png"
$icoPath = Join-Path $PSScriptRoot "icon.ico"
$sizes = 16, 24, 32, 48, 64, 128, 256

# Windows convention: the visible logo should fill ~92% of the icon canvas
# (about 4% padding on each side). The source PNG often has more padding than
# that, which makes the icon look tiny next to other apps on the desktop, so
# we auto-trim transparent borders before scaling each target size.
$paddingFraction = 0.04

$src = [System.Drawing.Image]::FromFile($srcPath)
$srcBmp = New-Object System.Drawing.Bitmap $src

# Find the bounding box of non-transparent pixels (alpha > 20). Step by 2 so a
# 1024x1024 source still scans in well under a second.
$minX = $src.Width; $minY = $src.Height; $maxX = 0; $maxY = 0
$step = 2
for ($y = 0; $y -lt $src.Height; $y += $step) {
    for ($x = 0; $x -lt $src.Width; $x += $step) {
        if ($srcBmp.GetPixel($x, $y).A -gt 20) {
            if ($x -lt $minX) { $minX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$visW = [Math]::Max(1, $maxX - $minX + 1)
$visH = [Math]::Max(1, $maxY - $minY + 1)
$visSide = [Math]::Max($visW, $visH)
Write-Output ("Trimmed source bbox: " + $visW + "x" + $visH + " (was " + $src.Width + "x" + $src.Height + ")")

$frames = @()
foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $target = [int]([Math]::Round($size * (1.0 - 2.0 * $paddingFraction)))
    $scale = $target / $visSide
    $drawW = [int]([Math]::Round($visW * $scale))
    $drawH = [int]([Math]::Round($visH * $scale))
    $dx = [int]([Math]::Round(($size - $drawW) / 2.0))
    $dy = [int]([Math]::Round(($size - $drawH) / 2.0))
    $srcRect = New-Object System.Drawing.Rectangle $minX, $minY, $visW, $visH
    $dstRect = New-Object System.Drawing.Rectangle $dx, $dy, $drawW, $drawH
    $g.DrawImage($src, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $frames += ,@{ size = $size; bytes = $ms.ToArray() }
}
$srcBmp.Dispose()
$src.Dispose()

# Hand-assemble the .ico container.
# Header: 6 bytes (reserved, type=1, count)
# Then per-frame ICONDIRENTRY: 16 bytes each (w, h, colors, reserved, planes, bpp, size, offset)
# Then the actual PNG payloads.
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0)              # reserved
$bw.Write([uint16]1)              # type 1 = icon
$bw.Write([uint16]$frames.Count)  # number of images
$dataOffset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $w = if ($f.size -ge 256) { 0 } else { $f.size }   # 256 stored as 0 per spec
    $h = $w
    $bw.Write([byte]$w)              # width
    $bw.Write([byte]$h)              # height
    $bw.Write([byte]0)               # colour palette count (0 for >256 colours)
    $bw.Write([byte]0)               # reserved
    $bw.Write([uint16]1)             # colour planes
    $bw.Write([uint16]32)            # bits per pixel
    $bw.Write([uint32]$f.bytes.Length)
    $bw.Write([uint32]$dataOffset)
    $dataOffset += $f.bytes.Length
}
foreach ($f in $frames) { $bw.Write($f.bytes) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $out.ToArray())
$bw.Dispose(); $out.Dispose()

$size = (Get-Item $icoPath).Length
Write-Output "Wrote $icoPath - $size bytes, $($frames.Count) frames (sizes: $($sizes -join ', '))"
