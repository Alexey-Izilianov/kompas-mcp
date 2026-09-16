# Pixel-art аватар «Давинчи» -> avatar.png (256x256) + avatar.ico (32+16).
# Мотив: робот-механик в берете (отсылка к да Винчи) на «пергаментном» круге.
# Grid: 16x16; '.' = прозрачный (углы круга).
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot

$grid = @(
  '.....KKKKKK.....',
  '...KKPPPPPPKK...',
  '..KPPPPPPPPPPK..',
  '..KPPBBBBBBPPK..',
  '.KPBBBBBBBBBBPK.',
  '.PKKKKKKKKKKKKP.',
  '.KDDDDDDDDDDDDK.',
  'KDDDEEDDDDDEEDDK',
  'KDDDEEDDDDDEEDDK',
  'KDDDDDDDDDDDDDDK',
  'KDDDDKKKKKKDDDDK',
  'KDDDDDDDDDDDDDDK',
  '.KDDDDDDDDDDDDK.',
  '..KDDDDDDDDDDK..',
  '...KPPPPPPPPK...',
  '.....KKKKKK.....'
)

# palette
$pal = @{
  'K' = @(40, 34, 30)      # контур (сепия-тёмный)
  'P' = @(232, 214, 168)   # пергамент
  'B' = @(150, 42, 42)     # берет (кирпично-красный)
  'D' = @(70, 80, 92)      # лицевая сталь
  'E' = @(90, 220, 255)    # глаза, циановое свечение
}

# sanity: все строки 16 символов, все символы известны
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
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  for ($y = 0; $y -lt 16; $y++) {
    for ($x = 0; $x -lt 16; $x++) {
      $ch = [string]$grid[$y][$x]
      if ($ch -eq '.') { continue }
      $c = $pal[$ch]
      $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $c[0], $c[1], $c[2]))
      $rect = New-Object System.Drawing.Rectangle ($x * $scale), ($y * $scale), $scale, $scale
      $g.FillRectangle($brush, $rect)
      $brush.Dispose()
    }
  }
  $g.Dispose()
  return $bmp
}

# PNG 256
$png = Render 16
$pngPath = Join-Path $dir 'avatar.png'
$png.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()
Write-Host "PNG OK: $pngPath"

# ICO with PNG-compressed 32 and 16 (Vista+ ICO)
$pngBytes32 = $null
$pngBytes16 = $null
$bmp32 = Render 2
$ms = New-Object System.IO.MemoryStream
$bmp32.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes32 = $ms.ToArray()
$ms.Dispose(); $bmp32.Dispose()
$bmp16 = Render 1
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