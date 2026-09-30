# Запуск smoke-скрипта через сервер: powershell -ExecutionPolicy Bypass -File run_smoke.ps1 <smoke.jsonl>
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
$exe = Join-Path (Get-Location) "KompasMcp.exe"
$file = $args[0]

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
$psi.WorkingDirectory = (Get-Location).Path
$p = [System.Diagnostics.Process]::Start($psi)

# UTF-8 пишем байтами: пайп PowerShell перекодирует в OEM и ломает кириллицу
$si = New-Object System.IO.StreamWriter($p.StandardInput.BaseStream, (New-Object System.Text.UTF8Encoding($false)))
$si.AutoFlush = $true
Get-Content $file -Encoding UTF8 | ForEach-Object { $si.WriteLine($_) }
$si.Dispose()

while (-not $p.StandardOutput.EndOfStream) {
  $r = $p.StandardOutput.ReadLine() | ConvertFrom-Json
  if ($r.result) {
    $txt = ""
    if ($r.result.content) { $txt = ($r.result.content | ForEach-Object { $_.text }) -join " | " }
    Write-Host ("[" + $r.id + "] isError=" + $r.result.isError + " " + $txt)
  }
  elseif ($r.error) {
    Write-Host ("[" + $r.id + "] rpc error " + $r.error.code + ": " + $r.error.message)
  }
}
$p.WaitForExit()
Write-Host "SMOKE DONE: $file"