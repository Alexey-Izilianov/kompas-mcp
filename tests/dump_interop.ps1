$ErrorActionPreference = 'Continue'
$libDir = "C:\Projects\kompas-test"

Write-Host "=== Kompas6API5: Document3D types, methods Part/Add/Open ==="
$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $libDir "Kompas6API5.dll"))
$types = $asm.GetTypes() | Where-Object { $_.Name -like "*Document3D*" }
foreach ($tt in $types) {
  Write-Host ("-- " + $tt.FullName)
  $tt.GetMethods() | Where-Object { $_.Name -match "Part|Add|Open" } | ForEach-Object {
    $ps = ($_.GetParameters() | ForEach-Object { $_.ParameterType.Name + " " + $_.Name }) -join ", "
    Write-Host ("   " + $_.Name + "(" + $ps + ")")
  }
}

Write-Host "=== KompasAPI7: interfaces with Assembly/Component ==="
$asm7 = [System.Reflection.Assembly]::LoadFrom((Join-Path $libDir "KompasAPI7.dll"))
$asm7.GetTypes() | Where-Object { $_.IsInterface -and $_.Name -match "Assembly|Component" } | ForEach-Object { Write-Host $_.FullName }

Write-Host "=== Kompas6Constants3D: types with Part/Assembly ==="
$asm3 = [System.Reflection.Assembly]::LoadFrom((Join-Path $libDir "Kompas6Constants3D.dll"))
$asm3.GetTypes() | Where-Object { $_.Name -match "Part_Type|Assembly" } | ForEach-Object { Write-Host $_.FullName }