# Assembly tools test (etap: sborki): own visible KOMPAS via kompas_spawn,
# server --attach, RPC: create_part -> save -> close -> create_assembly ->
# assembly_add_component -> assembly_components (count>=1).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe  = Join-Path (Split-Path $root) 'KompasMcp.exe'
$outDir = Join-Path (Split-Path (Split-Path $root)) 'mcp-out'

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
function Call-Tool([object]$proc, [int]$id, [string]$tool, [string]$argsJson) {
  $r = (Invoke-McpLines $p @('{"jsonrpc":"2.0","id":' + $id + ',"method":"tools/call","params":{"name":"' + $tool + '","arguments":' + $argsJson + '}}'))[0] | ConvertFrom-Json
  return $r
}

$spawn = Join-Path $root 'kompas_spawn.exe'
$spawnOut = & $spawn
$kpLine = $spawnOut | Where-Object { $_ -like 'PID=*' } | Select-Object -First 1
if (-not $kpLine) { throw "spawn helper returned no PID" }
$kp = [int]($kpLine.Substring(4))
Start-Sleep -Seconds 3
Write-Host "kompas pid: $kp"

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
  $init = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"assembly_test","version":"0"}}}'
  $r1 = Invoke-McpLines $p @($init, '{"jsonrpc":"2.0","id":2,"method":"ping","params":{}}') | ForEach-Object { $_ | ConvertFrom-Json }
  Check 'initialize' ($r1[0].result.serverInfo.name -eq 'kompas-mcp')

  $partPath = Join-Path $outDir 'asm_test_part.m3d'
  $asmPath  = Join-Path $outDir 'asm_test.asm.a3d'
  Remove-Item $partPath, $asmPath -ErrorAction SilentlyContinue

  # 1. деталь-компонент: создать + сохранить + закрыть
  $r2 = Call-Tool $p 3 'create_part' ('{"name":"TestPart","path":"' + ($partPath -replace '\\','\\') + '"}')
  Check 'create_part' ($r2.result.isError -ne $true)
  $r3 = Call-Tool $p 4 'save_part' ('{"path":"' + ($partPath -replace '\\','\\') + '"}')
  Check 'save_part' ($r3.result.isError -ne $true)
  $r4 = Call-Tool $p 5 'close_part' '{}'
  Check 'close_part' ($r4.result.isError -ne $true)

  # 2. сборка + компонент
  $r5 = Call-Tool $p 6 'create_assembly' ('{"name":"TestAssembly","path":"' + ($asmPath -replace '\\','\\') + '"}')
  Check 'create_assembly' ($r5.result.isError -ne $true)
  $r6 = Call-Tool $p 7 'assembly_add_component' ('{"path":"' + ($partPath -replace '\\','\\') + '","external":true}')
  if ($r6.result.isError) { Write-Host ("ERR: " + $r6.result.content[0].text) }
  Check 'assembly_add_component' ($r6.result.isError -ne $true)
  $compText = $r6.result.content[0].text
  Check 'component count 1' ($compText -like '*"count":1*')

  # 3. список
  $r7 = Call-Tool $p 7 'assembly_components' '{}'
  $compListText = $r7.result.content[0].text
  Write-Host ("components: " + $compListText)
  Check 'assembly_components' ($r7.result.isError -ne $true -and $compListText -like '*"count":1*')

  # 4. сохранить сборку
  $r8 = Call-Tool $p 8 'save_part' ('{"path":"' + ($asmPath -replace '\\','\\') + '"}')
  Check 'save assembly' ($r8.result.isError -ne $true)
}
finally {
  try { if (-not $p.HasExited) { $p.Kill() } } catch {}
  try { Stop-Process -Id $kp -Force -ErrorAction SilentlyContinue } catch {}
}
Write-Host 'ASSEMBLY TEST OK'