# Attach-mode protocol test (без КОМПАСа): сервер с --attach должен
# стартовать, отдавать mode:"attach" в kompas_status, запрещать
# kompas_start/kompas_show и не падать. Запуск: powershell (PS 5.1).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe  = Join-Path (Split-Path $root) 'KompasMcp.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe" }

function Invoke-McpLines([object]$proc, [string[]]$lines) {
  foreach ($l in $lines) { $proc.StandardInput.WriteLine($l) }
  $out = @()
  for ($i = 0; $i -lt $lines.Count; $i++) { $out += $proc.StandardOutput.ReadLine() }
  return ,$out
}

function Check([string]$name, [bool]$cond) {
  if (-not $cond) { throw "FAIL: $name" }
  Write-Host "[$name] ok"
}

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.Arguments = '--attach --rot-name KOMPAS_DAVINCHI_999999'
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
$p = [System.Diagnostics.Process]::Start($psi)
try {
  $init = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"attach_test","version":"0"}}}'
  $r1 = Invoke-McpLines $p @($init, '{"jsonrpc":"2.0","id":2,"method":"ping","params":{}}') | ForEach-Object { $_ | ConvertFrom-Json }
  Check 'initialize' ($r1[0].result.serverInfo.name -eq 'kompas-mcp')
  Check 'ping' ($null -ne $r1[1].result)

  $r2 = (Invoke-McpLines $p @('{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"kompas_status","arguments":{}}}'))[0] | ConvertFrom-Json
  Check 'status attach mode' ($r2.result.content[0].text -like '*"mode":"attach"*')
  Check 'status not running' ($r2.result.content[0].text -like '*"running":false*')

  $r3 = (Invoke-McpLines $p @('{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"kompas_start","arguments":{}}}'))[0] | ConvertFrom-Json
  Check 'kompas_start denied' ($r3.result.isError -eq $true -and $r3.result.content[0].text -like '*attach*')

  $r4 = (Invoke-McpLines $p @('{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"kompas_show","arguments":{"visible":true}}}'))[0] | ConvertFrom-Json
  Check 'kompas_show denied' ($r4.result.isError -eq $true -and $r4.result.content[0].text -like '*attach*')

  $r5 = (Invoke-McpLines $p @('{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"kompas_stop","arguments":{}}}'))[0] | ConvertFrom-Json
  Check 'kompas_stop detach' ($r5.result.isError -ne $true)

  $r6 = (Invoke-McpLines $p @('{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"create_drawing","arguments":{}}}'))[0] | ConvertFrom-Json
  Check 'create_drawing fails gracefully' ($r6.result.isError -eq $true -and $r6.result.content[0].text -like '*attach*')
}
finally {
  try { if (-not $p.HasExited) { $p.Kill() } } catch {}
}
Write-Host 'ATTACH TEST OK'