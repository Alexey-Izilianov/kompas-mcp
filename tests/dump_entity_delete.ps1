# Reflection dump: delete-related methods on ksEntity/ksPart (interop API-5).
$asm = [System.Reflection.Assembly]::LoadFrom("C:\Projects\kompas-test\Kompas6API5.dll")
foreach ($tn in @("ksEntity","ksPart")) {
  $t = $asm.GetType("Kompas6API5." + $tn)
  if ($t -eq $null) { Write-Host ($tn + ": type not found"); continue }
  Write-Host ("-- " + $tn)
  $t.GetMethods() | Where-Object { $_.Name -match "Delete|Remove|Rollback" } | ForEach-Object {
    $ps = ($_.GetParameters() | ForEach-Object { $_.ParameterType.Name + " " + $_.Name }) -join ", "
    Write-Host ("   " + $_.Name + "(" + $ps + ")")
  }
}