# Портативный архив dist\kompas-mcp-<версия>-win64.zip: сервер, библиотека «Давинчи»,
# документация, примеры сценариев. Interop-DLL ASCON в архив не входят — на месте их
# генерирует interop.ps1 из установленного КОМПАСа.
#   powershell -ExecutionPolicy Bypass -File package.ps1
$ErrorActionPreference = "Stop"
$dir = $PSScriptRoot

& powershell -ExecutionPolicy Bypass -File (Join-Path $dir "build.ps1")
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed" }
$exe = Join-Path $dir "KompasMcp.exe"
$ver = ((& $exe --version) -split ' ')[1]
& $exe --tools-md (Join-Path $dir "docs\tools.md")

$name = "kompas-mcp-$ver"
$dist = Join-Path $dir "dist"
$stage = Join-Path $dist $name
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage "davinci"), (Join-Path $stage "examples"), (Join-Path $stage "docs") | Out-Null

foreach ($f in @("KompasMcp.exe", "interop.ps1", "README.md")) { Copy-Item (Join-Path $dir $f) $stage }
foreach ($f in @("avatar.png", "avatar.ico")) { Copy-Item (Join-Path $dir "davinci\$f") $stage }
Copy-Item (Join-Path $dir "docs\*") (Join-Path $stage "docs") -Recurse
Get-ChildItem (Join-Path $dir "tests") -Filter "smoke_*.jsonl" | Where-Object { $_.Name -notlike "*_out.jsonl" } |
  Copy-Item -Destination (Join-Path $stage "examples")
Copy-Item (Join-Path $dir "tests\run_smoke.ps1") (Join-Path $stage "examples")
Copy-Item (Join-Path $dir "davinci\DavinciNative.rtw") (Join-Path $stage "davinci")
# шаблон конфига копилота: пустой kompasMcpExe — exe ищется уровнем выше библиотеки
$cfg = [ordered]@{ kompasMcpExe = ""; provider = "claude"; baseUrl = ""; apiKey = ""; model = ""; workspace = ""; timeoutSec = 900; claudeCmd = "claude.cmd"; env = @{} }
$cfg | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $stage "davinci\davinci.json")

$zip = Join-Path $dist "$name-win64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Host "PACKAGE OK: $zip"
