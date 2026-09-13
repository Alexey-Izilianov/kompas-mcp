$ErrorActionPreference = "Stop"
$dir   = $PSScriptRoot
$csc   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

# interop-DLL лежат в корне решения рядом с репозиторием (папка kompas-test)
# либо кладите их в lib\ рядом с build.ps1
$libDir = Join-Path $dir "lib"
if (-not (Test-Path $libDir)) { $libDir = Split-Path $dir -Parent }

$out = Join-Path $dir "KompasMcp.exe"
# src\davinci — это COM-библиотека «Давинчи» (собирается отдельно davinci\build-lib.ps1),
# в сервер не входит: она тянет interop KompasLibrary.dll
$sources = Get-ChildItem -Recurse (Join-Path $dir "src") -Filter *.cs |
  Where-Object { $_.FullName -notlike "*\davinci\*" } |
  ForEach-Object { $_.FullName }
$refs = @()
foreach ($n in @("Kompas6API5.dll","KompasAPI7.dll","Kompas6Constants.dll","Kompas6Constants3D.dll","KAPITypes.dll")) {
  $refs += "-r:" + (Join-Path $libDir $n)
}
# WinForms-панель Давинчи
$refs += "-r:System.Windows.Forms.dll"
$refs += "-r:System.Drawing.dll"

$argList = @("-nologo","-codepage:65001","-platform:x64","-target:exe","-optimize+","-out:$out") + $refs + $sources
& $csc @argList
if ($LASTEXITCODE -ne 0) { throw "csc failed: $LASTEXITCODE" }

# interop-DLL должны лежать рядом с exe
foreach ($n in @("Kompas6API5.dll","KompasAPI7.dll","Kompas6Constants.dll","Kompas6Constants3D.dll","KAPITypes.dll")) {
  Copy-Item (Join-Path $libDir $n) $dir -Force
}
Write-Host "BUILD OK: $out"