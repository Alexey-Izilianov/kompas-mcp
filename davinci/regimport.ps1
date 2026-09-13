$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
$src = Join-Path $dir 'Davinci.reg'
$dst = Join-Path $dir 'Davinci_hkcu.reg'
(Get-Content $src) -replace '\[HKEY_CLASSES_ROOT\\', '[HKEY_CURRENT_USER\Software\Classes\' |
  Set-Content $dst -Encoding Unicode
reg import $dst | Out-Null
foreach ($id in @('KompasMcp.Davinci', 'KompasMcp.DavinciLegacy')) {
  $t = [Type]::GetTypeFromProgID($id)
  if ($null -eq $t) { Write-Host "$id NOT FOUND" } else { Write-Host "$id OK -> $($t.FullName)" }
}