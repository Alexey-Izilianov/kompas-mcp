# Pixel-art avatar "Davinci" -> avatar.png (256x256) + avatar.ico (32+16).
# Grid: 16x16 chars; palette below. '.' = transparent.
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot

$grid = @(
  '......KK........',
  '.......K........',
  '.....KKKK.......',
  '....KGGGGK......',
  '...KGGGGGGK.....',
  '..KKKKKKKKKK....',
  '..KDDDDDDDDK....',
  '..KDEEDDEEDK....',
  '..KDDDDDDDDK....',
  '..KDDDKKKDDK....',
  '..KDDDKKKDDK....',
  '..KDDDDDDDDK....',
  '...KDDDDDDK.....',
  '....KDDDDK......',
  '....KKKKKK......',
  '...KGG..GGK.....'
)

# palette
$pal = @{
  'K' = @(30, 30, 40)      # outline dark
  'G' = @(0, 128, 96)      # Davinci green (antenna, ears)
  'D' = @(60, 70, 82)      # face plate steel
  'E' = @(80, 220, 255)    # eyes cyan glow
}

# sanity: all rows 16 chars, all chars known
if ($grid.Count -ne 16) { throw "grid must be 16 rows" }
foreach ($row in $grid) {
  if ($row.Length -ne 16) { throw ("row not 16 chars: [" + $row + "]") }
  foreach ($ch in $row.ToCharArray()) {
    if ($ch -ne '.' -and -not $pal.ContainsKey([string]$ch)) { throw ("unknown char: " + $ch) }
  }
}

Add-Type -AssemblyName System.Drawing

function Render([int]$scale) {
  $bmp = New-Object System.Drawing.Bitmap (16 * $scale), (16 * $scale)
  for ($y = 0; $y -lt 16; $y++) {
    for ($x = 0; $x -lt 16; $x++) {
      $ch = [string]$grid[$y][$x]
      if ($ch -eq '.') { continue }
      $c = $pal[$ch]
      $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $c[0], $c[1], $c[2]))
      $rect = New-Object System.Drawing.Rectangle ($x * $scale), ($y * $scale), $scale, $scale
      $g = [System.Drawing.Graphics]::FromImage($bmp)
      $g.FillRectangle($brush, $rect)
      $g.Dispose()
      $brush.Dispose()
    }
  }
  return $bmp
}

# rounded background: draw green rounded square behind (transparent corners)
function RenderCard([int]$scale) {
  $size = 16 * $scale
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $bgBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 20, 90, 70))
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
  $r = 2 * $scale
  # rounded rect path
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0, 0, 2*$r, 2*$r, 180, 90)
  $path.AddArc($size - 2*$r, 0, 2*$r, 2*$r, 270, 90)
  $path.AddArc($size - 2*$r, $size - 2*$r, 2*$r, 2*$r, 0, 90)
  $path.AddArc(0, $size - 2*$r, 2*$r, 2*$r, 90, 90)
  $path.CloseFigure()
  $g.FillPath($bgBrush, $path)
  $g.Dispose()
  # robot pixels on top (skip '.' but paint over bg)
  for ($y = 0; $y -lt 16; $y++) {
    for ($x = 0; $x -lt 16; $x++) {
      $ch = [string]$grid[$y][$x]
      if ($ch -eq '.') { continue }
      $c = $pal[$ch]
      $b = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $c[0], $c[1], $c[2]))
      $rect = New-Object System.Drawing.Rectangle ($x * $scale), ($y * $scale), $scale, $scale
      $g2 = [System.Drawing.Graphics]::FromImage($bmp)
      $g2.FillRectangle($b, $rect)
      $g2.Dispose()
      $b.Dispose()
    }
  }
  return $bmp
}

# PNG 256
$png = RenderCard 16
$pngPath = Join-Path $dir 'avatar.png'
$png.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()
Write-Host "PNG OK: $pngPath"

# ICO with PNG-compressed 32 and 16 (Vista+ ICO)
$pngBytes32 = $null
$pngBytes16 = $null
$bmp32 = RenderCard 2
$ms = New-Object System.IO.MemoryStream
$bmp32.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes32 = $ms.ToArray()
$ms.Dispose(); $bmp32.Dispose()
$bmp16 = RenderCard 1
$ms = New-Object System.IO.MemoryStream
$bmp16.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes16 = $ms.ToArray()
$ms.Dispose(); $bmp16.Dispose()

$icoPath = Join-Path $dir 'avatar.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]2)   # ICONDIR: 2 images
$offset = 6 + 2 * 16                                                # 2 entries * 16 bytes
# entry: width, height, colors, reserved, planes, bitcount, size, offset
$bw.Write([byte]32); $bw.Write([byte]32); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([uint16]1); $bw.Write([uint16]32)
$bw.Write([uint32]$pngBytes32.Length); $bw.Write([uint32]$offset)
$offset += $pngBytes32.Length
$bw.Write([byte]16); $bw.Write([byte]16); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([uint16]1); $bw.Write([uint16]32)
$bw.Write([uint32]$pngBytes16.Length); $bw.Write([uint32]$offset)
$offset += $pngBytes16.Length
$bw.Write($pngBytes32); $bw.Write($pngBytes16)
$bw.Dispose(); $fs.Dispose()
Write-Host "ICO OK: $icoPath"