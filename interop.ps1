# Interop-сборки КОМПАС-3D для KompasMcp.exe и библиотеки «Давинчи».
# DLL ASCON распространять нельзя, поэтому они генерируются на месте из библиотек типов
# установленного КОМПАСа (<КОМПАС>\SDK\lib\*.tlb) утилитой TlbImp из Windows SDK.
#   powershell -ExecutionPolicy Bypass -File interop.ps1 [-Target <папка>]
# По умолчанию — в папку скрипта (и в её davinci\, если есть).
param([string]$Target = $PSScriptRoot)
$ErrorActionPreference = "Stop"

function Find-KompasRoot {
  $clsid = (Get-ItemProperty "Registry::HKEY_CLASSES_ROOT\KOMPAS.Application.5\CLSID" -ErrorAction SilentlyContinue).'(default)'
  if (-not $clsid) { throw "КОМПАС-3D не зарегистрирован в системе (нет KOMPAS.Application.5). Установите КОМПАС и запустите его один раз." }
  $server = (Get-ItemProperty "Registry::HKEY_CLASSES_ROOT\CLSID\$clsid\LocalServer32").'(default)'
  if ($server -match '^"([^"]+)"') { $exe = $Matches[1] } else { $exe = ($server -split ' /')[0] }
  return Split-Path (Split-Path $exe -Parent) -Parent
}

function Find-TlbImp {
  $roots = @("${env:ProgramFiles(x86)}\Microsoft SDKs\Windows", "$env:ProgramFiles\Microsoft SDKs\Windows")
  foreach ($r in $roots) {
    if (-not (Test-Path $r)) { continue }
    $found = Get-ChildItem $r -Recurse -Filter TlbImp.exe -ErrorAction SilentlyContinue |
      Where-Object { $_.DirectoryName -like "*\x64" } | Sort-Object FullName -Descending | Select-Object -First 1
    if ($found) { return $found.FullName }
  }
  throw "Не найден TlbImp.exe. Установите Windows SDK (компонент «.NET Framework 4.8 SDK» / NETFX Tools) или положите interop-DLL из SDK КОМПАС рядом с KompasMcp.exe вручную."
}

$root = Find-KompasRoot
$tlb = Join-Path $root "SDK\lib"
foreach ($n in @("kAPI5.tlb", "kAPI7.tlb", "ksConstants.tlb", "ksConstants3D.tlb")) {
  if (-not (Test-Path (Join-Path $tlb $n))) { throw "Нет $tlb\$n — установите КОМПАС-3D вместе с SDK." }
}
$tlbimp = Find-TlbImp
Write-Host "КОМПАС: $root"
Write-Host "TlbImp: $tlbimp"

# имена сборок и пространств имён — как у interop из SDK, с которыми собран KompasMcp.exe
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("kompas-interop-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $tmp | Out-Null
Push-Location $tmp
try {
  $jobs = @(
    @("ksConstants.tlb", "Kompas6Constants", @()),
    @("ksConstants3D.tlb", "Kompas6Constants3D", @()),
    @("kAPI5.tlb", "Kompas6API5", @("Kompas6Constants.dll", "Kompas6Constants3D.dll")),
    @("kAPI7.tlb", "KompasAPI7", @("Kompas6Constants.dll", "Kompas6Constants3D.dll", "Kompas6API5.dll"))
  )
  if (Test-Path (Join-Path $tlb "KompasLibrary.tlb")) { $jobs += ,@("KompasLibrary.tlb", "KompasLibrary", @("Kompas6API5.dll")) }
  foreach ($j in $jobs) {
    $refs = @($j[2] | ForEach-Object { "/reference:$_" })
    & $tlbimp (Join-Path $tlb $j[0]) "/out:$($j[1]).dll" "/namespace:$($j[1])" /asmversion:1.0.0.0 /silent @refs
    if ($LASTEXITCODE -ne 0) { throw "TlbImp: ошибка на $($j[0])" }
  }
  # KAPITypes (константы ldefin2d) нужна только при сборке из исходников — берём из поставки
  $kapi = Join-Path $root "Libs\IOSO\KAPITypes.dll"
  if (Test-Path $kapi) { Copy-Item $kapi $tmp }

  $targets = @($Target)
  $davinci = Join-Path $Target "davinci"
  if (Test-Path (Join-Path $davinci "DavinciNative.rtw")) { $targets += $davinci }
  foreach ($t in $targets) {
    New-Item -ItemType Directory -Force $t | Out-Null
    Copy-Item (Join-Path $tmp "*.dll") $t -Force
    Write-Host "interop -> $t"
  }
}
finally {
  Pop-Location
  Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
