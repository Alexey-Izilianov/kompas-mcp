# Аватар «Давинчи»: avatar.svg (портрет Леонардо) -> avatar.png (256) + avatar.ico (16..256).
# SVG растеризуется headless-браузером (Edge или Chrome) в 1024 px, затем уменьшается
# ступенями по 2 — так мелкие размеры иконки остаются чёткими.
#   powershell -ExecutionPolicy Bypass -File davinci\gen_avatar.ps1
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
$svg = Join-Path $dir 'avatar.svg'

$browser = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
             "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
             "$env:ProgramFiles\Google\Chrome\Application\chrome.exe") |
  Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $browser) { throw "Нужен Microsoft Edge или Google Chrome для растеризации SVG" }

$work = Join-Path ([IO.Path]::GetTempPath()) ("davinci-avatar-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $work | Out-Null
try {
  $html = Join-Path $work 'page.html'
  $big = Join-Path $work 'avatar1024.png'
  $svgText = Get-Content $svg -Raw -Encoding UTF8
  ("<!doctype html><html><head><meta charset='utf-8'><style>html,body{margin:0;background:transparent;overflow:hidden}" +
   "svg{display:block;width:1024px;height:1024px}</style></head><body>" + $svgText + "</body></html>") |
    Set-Content $html -Encoding UTF8
  # браузер пишет отчёт в stderr — при Stop PowerShell счёл бы это ошибкой
  $ErrorActionPreference = 'Continue'
  & $browser --headless=new --disable-gpu --hide-scrollbars --user-data-dir="$work\profile" `
    --default-background-color=00000000 --window-size=1024,1024 --screenshot="$big" `
    ("file:///" + ($html -replace '\\', '/')) 2>&1 | Out-Null
  $ErrorActionPreference = 'Stop'
  if (-not (Test-Path $big)) { throw "Браузер не создал растр SVG" }

  Add-Type -AssemblyName System.Drawing
  $src = [System.Drawing.Bitmap]::FromFile($big)

  function Scale([System.Drawing.Bitmap]$b, [int]$size) {
    $r = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($r)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($b, 0, 0, $size, $size)
    $g.Dispose()
    return $r
  }

  # ступенчатое уменьшение: каждый шаг не больше чем вдвое
  function Downscale([System.Drawing.Bitmap]$b, [int]$size) {
    $cur = $b
    while ($cur.Width -gt 2 * $size) {
      $next = Scale $cur ([int][Math]::Max($size, [Math]::Floor($cur.Width / 2)))
      if ($cur -ne $b) { $cur.Dispose() }
      $cur = $next
    }
    $res = Scale $cur $size
    if ($cur -ne $b) { $cur.Dispose() }
    return $res
  }

  function PngBytes([System.Drawing.Bitmap]$b) {
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray(); $ms.Dispose()
    return ,$bytes
  }

  $png = Downscale $src 256
  $png.Save((Join-Path $dir 'avatar.png'), [System.Drawing.Imaging.ImageFormat]::Png)
  Write-Host "PNG OK: $(Join-Path $dir 'avatar.png')"

  # ICO из PNG-кадров (Vista+): 256 записывается как 0 в байтах ширины/высоты
  $sizes = @(256, 48, 32, 24, 16)
  $frames = @()
  foreach ($s in $sizes) {
    $b = if ($s -eq 256) { $png } else { Downscale $src $s }
    $frames += ,(PngBytes $b)
    if ($s -ne 256) { $b.Dispose() }
  }
  $png.Dispose(); $src.Dispose()

  $ico = Join-Path $dir 'avatar.ico'
  $fs = [System.IO.File]::Create($ico)
  $bw = New-Object System.IO.BinaryWriter $fs
  $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  for ($i = 0; $i -lt $sizes.Count; $i++) {
    $wh = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$wh); $bw.Write([byte]$wh); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$frames[$i].Length); $bw.Write([uint32]$offset)
    $offset += $frames[$i].Length
  }
  foreach ($f in $frames) { $bw.Write($f) }
  $bw.Dispose(); $fs.Dispose()
  Write-Host "ICO OK: $ico"
}
finally {
  Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
