# Сборка нативно-совместимой .rtw-библиотеки «Давинчи» без C++-компилятора:
# csc -> ildasm -> inject_exports.py (.export) -> ilasm (+ rc-ресурсы).
# ВНИМАНИЕ: если КОМПАС подключил Davinci.rtw, файл заблокирован процессом —
# пересборку делать в DavinciNative.rtw (скрипт так и пишет) или перезапустить КОМПАС.
$ErrorActionPreference = "Stop"
$dir = $PSScriptRoot
$net = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc = "$net\csc.exe"
$ilasm = "$net\ilasm.exe"
$ildasm = "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\x64\ildasm.exe"
$rc = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\rc.exe"

$libDir = Split-Path (Split-Path $dir -Parent) -Parent
if (-not (Test-Path (Join-Path $libDir "KompasLibrary.dll"))) { $libDir = Join-Path (Split-Path (Split-Path $dir -Parent) -Parent) "kompas-test" }
if (-not (Test-Path (Join-Path $libDir "KompasLibrary.dll"))) { throw "interop KompasLibrary.dll не найден" }

# 1) C# -> IL-DLL
$out = Join-Path $dir "DavinciNative.dll"
& $csc -nologo -codepage:65001 -platform:x64 -target:library -optimize+ -out:$out `
  "-r:$(Join-Path $libDir 'KompasLibrary.dll')" `
  "-r:$(Join-Path $libDir 'Kompas6API5.dll')" `
  (Join-Path $dir "src\DavinciLib.cs") (Join-Path $dir "src\DavinciNative.cs")
if ($LASTEXITCODE -ne 0) { throw "csc failed: $LASTEXITCODE" }

# 2) IL-дамп (/utf8 — иначе ildasm пишет в системной кодировке) и экспорты
$il = Join-Path $dir "DavinciNative.il"
& $ildasm /nobar /utf8 /out=$il $out
if ($LASTEXITCODE -ne 0) { throw "ildasm failed: $LASTEXITCODE" }
python (Join-Path $dir "inject_exports.py") $il (Join-Path $dir "DavinciNative_exports.il")

# 3) Win32-ресурсы: меню библиотеки + имя (STRINGTABLE)
$res = Join-Path $dir "lib.res"
& $rc /nologo /fo $res (Join-Path $dir "lib.rc")
if ($LASTEXITCODE -ne 0) { throw "rc failed: $LASTEXITCODE" }

# 4) ilasm -> PE32+ .rtw (x64); /pe64 без /x64 даёт ITANIUM!
$rtw = Join-Path $dir "DavinciNative.rtw"
& $ilasm /nologo /quiet /dll /pe64 /x64 /resource=$res /output=$rtw (Join-Path $dir "DavinciNative_exports.il")
if ($LASTEXITCODE -ne 0) { throw "ilasm failed: $LASTEXITCODE" }
Write-Host "BUILD OK: $rtw"

# interop рядом с DLL для загрузчика КОМПАСа
foreach ($n in @("KompasLibrary.dll", "Kompas6API5.dll", "Kompas6Constants.dll", "KAPITypes.dll")) {
  $src = Join-Path $libDir $n
  if (Test-Path $src) { Copy-Item $src $dir -Force }
}