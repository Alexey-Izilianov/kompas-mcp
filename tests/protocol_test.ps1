$ErrorActionPreference = "Stop"
$exe = Join-Path (Split-Path $PSScriptRoot -Parent) "KompasMcp.exe"

# 1) Протокол без КОМПАСа: initialize, ping, tools/list, tools/call ping,
#    kompas_status (не должен запускать КОМПАС), неизвестный метод.
$in = @'
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"ping"}
{"jsonrpc":"2.0","id":3,"method":"tools/list"}
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"ping","arguments":{}}}
{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"kompas_status","arguments":{}}}
{"jsonrpc":"2.0","id":6,"method":"no/such/method"}
'@

$in -split "`n" | & $exe | ForEach-Object {
  $r = $_ | ConvertFrom-Json
  if ($r.id -eq 1) { Write-Host ("[1 initialize] version=" + $r.result.protocolVersion + " server=" + $r.result.serverInfo.name) }
  elseif ($r.id -eq 2) { Write-Host "[2 ping] ok" }
  elseif ($r.id -eq 3) { Write-Host ("[3 tools/list] count=" + $r.result.tools.Count + " names=" + (($r.result.tools | ForEach-Object { $_.name }) -join ", ")) }
  elseif ($r.id -eq 4) { Write-Host ("[4 call ping] isError=" + $r.result.isError + " text=" + $r.result.content[0].text) }
  elseif ($r.id -eq 5) { Write-Host ("[5 status] isError=" + $r.result.isError + " text=" + $r.result.content[0].text) }
  elseif ($r.id -eq 6) { Write-Host ("[6 unknown] error=" + $r.error.code + " " + $r.error.message) }
}
Write-Host "PROTOCOL TEST OK"