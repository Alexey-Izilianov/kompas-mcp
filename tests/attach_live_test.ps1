# Attach to LIVE KOMPAS (etap 3 experiment ROT/GetActiveObject): own visible
# KOMPAS instance -> KompasMcp.exe --attach (no rot-name) -> status shows
# running=true and pid; kompas_stop = detach, KOMPAS process must survive.
# Run: powershell (PS 5.1).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe  = Join-Path (Split-Path $root) 'KompasMcp.exe'

function Check([string]$name, [bool]$cond) {
  if (-not $cond) { throw "FAIL: $name" }
  Write-Host "[$name] ok"
}
function Get-Pids { @(Get-Process -Name KOMPAS -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) }
function Invoke-McpLines([object]$proc, [string[]]$lines) {
  foreach ($l in $lines) { $proc.StandardInput.WriteLine($l) }
  $out = @()
  for ($i = 0; $i -lt $lines.Count; $i++) { $out += $proc.StandardOutput.ReadLine() }
  return ,$out
}

$spawn = Join-Path $root 'kompas_spawn.exe'
if (-not (Test-Path $spawn)) { throw "kompas_spawn.exe not found - build kompas_spawn.cs first" }
$spawnOut = & $spawn
$kpLine = $spawnOut | Where-Object { $_ -like 'PID=*' } | Select-Object -First 1
if (-not $kpLine) { throw "spawn helper returned no PID" }
$kp = [int]($kpLine.Substring(4))
Start-Sleep -Seconds 4
$before = Get-Pids
Write-Host "kompas pids: [$($before -join ',')]"
if ($before.Count -ne 1) { throw "expected exactly one KOMPAS process" }

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.Arguments = '--attach'
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
$p = [System.Diagnostics.Process]::Start($psi)
try {
  $init = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"attach_live_test","version":"0"}}}'
  $r1 = Invoke-McpLines $p @($init, '{"jsonrpc":"2.0","id":2,"method":"ping","params":{}}') | ForEach-Object { $_ | ConvertFrom-Json }
  Check 'initialize' ($r1[0].result.serverInfo.name -eq 'kompas-mcp')

  $r2 = (Invoke-McpLines $p @('{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"kompas_status","arguments":{}}}'))[0] | ConvertFrom-Json
  $st = $r2.result.content[0].text
  Check 'status attach mode' ($st -like '*"mode":"attach"*')
  Check 'status running' ($st -like '*"running":true*')
  Check 'status pid' ($st -like "*$kp*")

  # stop = detach: KOMPAS process must survive
  $r5 = (Invoke-McpLines $p @('{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"kompas_stop","arguments":{}}}'))[0] | ConvertFrom-Json
  Check 'kompas_stop detach' ($r5.result.isError -ne $true)
  Start-Sleep -Seconds 2
  Check 'kompas still alive' ((Get-Pids) -contains $kp)
}
finally {
  try { if (-not $p.HasExited) { $p.Kill() } } catch {}
  foreach ($pr in Get-Process -Name KOMPAS -ErrorAction SilentlyContinue) {
    try { $pr.Kill() } catch {}
  }
}
Write-Host 'ATTACH LIVE TEST OK'