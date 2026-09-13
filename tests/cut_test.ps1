# Cut verification test: own visible KOMPAS via kompas_spawn, server --attach.
# Boss block 100x50x20 on XOY, then blind cut through the block:
# the cut MUST reduce mass (previously blind cut returned cut:true with no effect).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe  = Join-Path (Split-Path $root) 'KompasMcp.exe'
$outDir = Join-Path (Split-Path (Split-Path $root)) 'mcp-out'

function Check([string]$name, [bool]$cond) {
  if (-not $cond) { throw "FAIL: $name" }
  Write-Host "[$name] ok"
}
function Invoke-McpLines([object]$proc, [string[]]$lines) {
  foreach ($l in $lines) { $proc.StandardInput.WriteLine($l) }
  $out = @()
  for ($i = 0; $i -lt $lines.Count; $i++) { $out += $proc.StandardOutput.ReadLine() }
  return ,$out
}
function Call-Tool([object]$proc, [int]$id, [string]$tool, [string]$argsJson) {
  $r = (Invoke-McpLines $p @('{"jsonrpc":"2.0","id":' + $id + ',"method":"tools/call","params":{"name":"' + $tool + '","arguments":' + $argsJson + '}}'))[0] | ConvertFrom-Json
  if ($r.result.isError) { Write-Host ("ERR: " + $r.result.content[0].text) }
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
  $init = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"cut_test","version":"0"}}}'
  $r1 = Invoke-McpLines $p @($init, '{"jsonrpc":"2.0","id":2,"method":"ping","params":{}}') | ForEach-Object { $_ | ConvertFrom-Json }
  Check 'initialize' ($r1[0].result.serverInfo.name -eq 'kompas-mcp')

  # база: блок 100x50x20 (эскиз на XOY, boss depth 20)
  $partPath = Join-Path $outDir 'cut_test.m3d'
  $r2 = Call-Tool $p 3 'create_part' ('{"name":"CutTest","path":"' + ($partPath -replace '\\','\\') + '"}')
  Check 'create_part' ($r2.result.isError -ne $true)
  $r3 = Call-Tool $p 4 'sketch' ('{"plane":"XOY","elements":[{"type":"line","x1":0,"y1":0,"x2":100,"y2":0},{"type":"line","x1":100,"y2":0,"x2":100,"y1":50},{"type":"line","x1":100,"y1":50,"x2":0,"y2":50},{"type":"line","x1":0,"y2":50,"x2":0,"y1":0}]}')
  Check 'sketch base' ($r3.result.isError -ne $true)
  $r4 = Call-Tool $p 5 'extrude_boss' '{"depth":20,"base":true}'
  Check 'boss base' ($r4.result.isError -ne $true)
  $mBase = [double]($r4.result.content[0].text | ConvertFrom-Json).mass_kg
  Write-Host ("mass base: " + $mBase)
  Check 'base mass positive' ($mBase -gt 0)

  # слепой вырез квадратом 40x40 в центре верхней грани... эскиз на offset XOY z=20, вырез вниз (слепо, depth 5)
  $r5 = Call-Tool $p 6 'sketch' ('{"plane":"XOY","offset":20,"elements":[{"type":"line","x1":30,"y1":5,"x2":70,"y2":5},{"type":"line","x1":70,"y2":5,"x2":70,"y1":45},{"type":"line","x1":70,"y1":45,"x2":30,"y2":45},{"type":"line","x1":30,"y2":45,"x2":30,"y1":5}]}')
  Check 'sketch cut' ($r5.result.isError -ne $true)
  $r6 = Call-Tool $p 7 'extrude_cut' '{"mode":"blind","depth":5}'
  Check 'blind cut reduces mass' ($r6.result.isError -ne $true)
  $mCut = [double]($r6.result.content[0].text | ConvertFrom-Json).mass_kg
  Write-Host ("mass after blind cut: " + $mCut)
  Check 'mass decreased' ($mCut -lt ($mBase - 1e-6))

  # сквозной вырез — тоже должен работать (проверенный рецепт)
  $r7 = Call-Tool $p 8 'sketch' ('{"plane":"XOY","elements":[{"type":"circle","xc":50,"yc":25,"r":5}]}')
  $r8 = Call-Tool $p 9 'extrude_cut' '{"mode":"through"}'
  Check 'through cut reduces mass' ($r8.result.isError -ne $true)
  $mThrough = [double]($r8.result.content[0].text | ConvertFrom-Json).mass_kg
  Write-Host ("mass after through cut: " + $mThrough)
  Check 'through mass decreased' ($mThrough -lt ($mCut - 1e-6))
}
finally {
  try { if (-not $p.HasExited) { $p.Kill() } } catch {}
  try { Stop-Process -Id $kp -Force -ErrorAction SilentlyContinue } catch {}
}
Write-Host 'CUT TEST OK'