$ErrorActionPreference = "Stop"
# Recovery: the file was UTF-8, read as CP1251 and written back as UTF-8.
# Detection: CP1251 re-encoding of the text must yield valid UTF-8 and contain non-ASCII.
$enc1251 = [System.Text.Encoding]::GetEncoding(1251)
$strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
$plainUtf8 = New-Object System.Text.UTF8Encoding($false)
Get-ChildItem -Path $PSScriptRoot -Include *.jsonl,*.ps1 -Recurse | ForEach-Object {
  $text = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
  $hasHigh = $false
  foreach ($ch in $text.ToCharArray()) { if ([int]$ch -gt 127) { $hasHigh = $true; break } }
  if (-not $hasHigh) { Write-Host "skip  $($_.Name)"; return }
  $bytes = $enc1251.GetBytes($text)
  try {
    $restored = $strictUtf8.GetString($bytes)
  } catch { Write-Host "clean $($_.Name)"; return }
  if (-not ($restored -match '[Ѐ-ӿ]')) { Write-Host "clean $($_.Name)"; return }
  [System.IO.File]::WriteAllText($_.FullName, $restored, $plainUtf8)
  Write-Host "fixed $($_.Name)"
}