# Сборка COM-библиотеки «Давинчи» (.NET 4, csc) и регистрация (regasm /codebase).
# regasm пишет в HKLM — запускать из shell с правами админа.
$ErrorActionPreference = "Stop"
$dir = $PSScriptRoot                       # kompas-mcp\davinci
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$regasm = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

# interop-DLL — в корне kompas-test (родитель kompas-mcp)
$libDir = Split-Path (Split-Path $dir -Parent) -Parent
if (-not (Test-Path (Join-Path $libDir "KompasLibrary.dll"))) { $libDir = Join-Path (Split-Path (Split-Path $dir -Parent) -Parent) "kompas-test" }
if (-not (Test-Path (Join-Path $libDir "KompasLibrary.dll"))) { throw "interop KompasLibrary.dll не найден" }

$out = Join-Path $dir "KompasMcp.Davinci.dll"
$refLib = "-r:" + (Join-Path $libDir "KompasLibrary.dll")
$refApi = "-r:" + (Join-Path $libDir "Kompas6API5.dll")
$src = Join-Path $dir "src\DavinciLib.cs"
$argList = @("-nologo", "-codepage:65001", "-platform:x64", "-target:library", "-optimize+", "-out:$out", $refLib, $refApi, $src)
& $csc @argList
if ($LASTEXITCODE -ne 0) { throw "csc failed: $LASTEXITCODE" }
Write-Host "BUILD OK: $out"

# interop рядом с DLL, чтобы regasm и загрузчик КОМПАСа их нашли
foreach ($n in @("KompasLibrary.dll", "Kompas6API5.dll", "Kompas6Constants.dll", "KAPITypes.dll")) {
  $src = Join-Path $libDir $n
  if (Test-Path $src) { Copy-Item $src $dir -Force }
}

# .rtw-копия для ksAttachKompasLibrary (эксперимент: грузит ли v22 managed-DLL)
Copy-Item $out (Join-Path $dir "Davinci.rtw") -Force

& $regasm $out /codebase /nologo
if ($LASTEXITCODE -ne 0) { throw "regasm failed: $LASTEXITCODE (нужны права админа)" }
Write-Host "REGASM OK"