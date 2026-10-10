$ErrorActionPreference = "Stop"
$dir   = $PSScriptRoot
$csc   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

# interop-DLL: lib\ рядом с build.ps1, затем папка уровнем выше; если нет нигде —
# генерируются из библиотек типов установленного КОМПАСа (interop.ps1 -> lib\)
$libDir = Join-Path $dir "lib"
if (-not (Test-Path (Join-Path $libDir "Kompas6API5.dll"))) {
  $parent = Split-Path $dir -Parent
  if (Test-Path (Join-Path $parent "Kompas6API5.dll")) { $libDir = $parent }
  else {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $dir "interop.ps1") -Target $libDir
    if ($LASTEXITCODE -ne 0) { throw "interop.ps1 failed" }
  }
}

$out = Join-Path $dir "KompasMcp.exe"
# библиотека КОМПАСа «Давинчи» (davinci\src) собирается отдельно — davinci\build-native.ps1 —
# и в сервер не входит: тянет interop KompasLibrary.dll
$sources = Get-ChildItem -Recurse (Join-Path $dir "src") -Filter *.cs | ForEach-Object { $_.FullName }
$refs = @()
foreach ($n in @("Kompas6API5.dll","KompasAPI7.dll","Kompas6Constants.dll","Kompas6Constants3D.dll","KAPITypes.dll")) {
  $refs += "-r:" + (Join-Path $libDir $n)
}
# WPF-панель Давинчи (сборки из каталога .NET Framework, рядом с csc)
foreach ($n in @("WPF\PresentationCore.dll","WPF\PresentationFramework.dll","WPF\WindowsBase.dll","System.Xaml.dll")) {
  $refs += "-r:" + (Join-Path (Split-Path $csc -Parent) $n)
}

$argList = @("-nologo","-codepage:65001","-platform:x64","-target:exe","-optimize+","-out:$out")
# аватар Давинчи (портрет, davinci\gen_avatar.ps1) как иконка exe
$ico = Join-Path $dir "davinci\avatar.ico"
if (Test-Path $ico) { $argList += "/win32icon:$ico" }
$argList += $refs + $sources
& $csc @argList
if ($LASTEXITCODE -ne 0) { throw "csc failed: $LASTEXITCODE" }

# аватар рядом с exe (шапка панели чата)
foreach ($n in @("avatar.png","avatar.ico")) {
  $src = Join-Path $dir ("davinci\" + $n)
  if (Test-Path $src) { Copy-Item $src $dir -Force }
}

# interop-DLL должны лежать рядом с exe
foreach ($n in @("Kompas6API5.dll","KompasAPI7.dll","Kompas6Constants.dll","Kompas6Constants3D.dll","KAPITypes.dll")) {
  Copy-Item (Join-Path $libDir $n) $dir -Force
}
Write-Host "BUILD OK: $out"