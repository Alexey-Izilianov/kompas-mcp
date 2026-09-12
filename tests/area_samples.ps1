$ErrorActionPreference = "Stop"
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$m = 2.0; $z = 20
$alpha = [math]::PI * 20.0 / 180.0
$d = $m * $z; $rb = $d * [math]::Cos($alpha) / 2.0
$s = [math]::PI * $m / 2.0
$invA = [math]::Tan($alpha) - $alpha
function Inv([double]$t) { [math]::Tan($t) - $t }
foreach ($samples in 1,3,6) {
  $ra = 22.0; $rf = 17.5; $rStart = [math]::Max($rb, $rf)
  $step = 2 * [math]::PI / $z
  $psiBase = $s / $d + $invA
  $X = New-Object System.Collections.ArrayList; $Y = New-Object System.Collections.ArrayList
  for ($t = 0; $t -lt $z; $t++) {
    $c = $step * $t
    for ($i = 0; $i -le $samples; $i++) {
      $r = $rStart + ($ra - $rStart) * $i / $samples
      $psi = $s / $d + $invA - (Inv ([math]::Acos($rb / $r)))
      if ($r -le $rb) { $psi = $psiBase }
      [void]$X.Add($r * [math]::Cos($c - $psi)); [void]$Y.Add($r * [math]::Sin($c - $psi))
    }
    $psiTop = $s / $d + $invA - (Inv ([math]::Acos($rb / $ra)))
    [void]$X.Add($ra * [math]::Cos($c + $psiTop)); [void]$Y.Add($ra * [math]::Sin($c + $psiTop))
    for ($i = $samples - 1; $i -ge 0; $i--) {
      $r = $rStart + ($ra - $rStart) * $i / $samples
      $psi = $s / $d + $invA - (Inv ([math]::Acos($rb / $r)))
      if ($r -le $rb) { $psi = $psiBase }
      [void]$X.Add($r * [math]::Cos($c + $psi)); [void]$Y.Add($r * [math]::Sin($c + $psi))
    }
    [void]$X.Add($rf * [math]::Cos($c + $psiBase)); [void]$Y.Add($rf * [math]::Sin($c + $psiBase))
  }
  $A = 0.0
  for ($i = 0; $i -lt $X.Count; $i++) {
    $j = ($i + 1) % $X.Count
    $A += $X[$i] * $Y[$j] - $X[$j] * $Y[$i]
  }
  $A = [math]::Abs($A) / 2.0
  ("samples=" + $samples + " area=" + $A.ToString("F2", $inv) + " mass=" + ($A * 20 * 7.85e-6).ToString("F6", $inv))
}