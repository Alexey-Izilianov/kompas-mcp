# Сборка encoding_test: все исходники сервера + e2e_test.cs (-main:EncodingTest)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$sources = Get-ChildItem -Recurse (Join-Path $root "src") -Filter *.cs |
  ForEach-Object { $_.FullName }
$sources += (Join-Path $PSScriptRoot "encoding_test.cs")
$libDir = Split-Path $root -Parent
$refs = @()
foreach ($n in @("Kompas6API5.dll","KompasAPI7.dll","Kompas6Constants.dll","Kompas6Constants3D.dll","KAPITypes.dll")) {
  $refs += "-r:" + (Join-Path $libDir $n)
}
$refs += @("-r:System.Windows.Forms.dll", "-r:System.Drawing.dll")
$out = Join-Path $PSScriptRoot "encoding_test.exe"
$argList = @("-nologo","-codepage:65001","-platform:x64","-main:EncodingTest","-out:$out") + $refs + $sources
& $csc @argList
if ($LASTEXITCODE -ne 0) { throw "csc failed: $LASTEXITCODE" }
Copy-Item (Join-Path $libDir "Kompas6API5.dll") $PSScriptRoot -Force -ErrorAction SilentlyContinue
Write-Host "E2E TEST BUILD OK"