$ErrorActionPreference = "Stop"
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$m = 2.0; $z = 20
$alpha = [math]::PI * 20.0 / 180.0
$d = $m * $z; $rb = $d * [math]::Cos($alpha) / 2.0
$s = [math]::PI * $m / 2.0
$invA = [math]::Tan($alpha) - $alpha
function Inv([double]$t) { [math]::Tan($t) - $t }
# Correct profile: tooth centered at c=k*step, flanks c +/- psi(r), psi = s/d + invA - inv(acos(rb/r))
function AreaCorrect([int]$zArg, [double]$mArg) {
  $dA = $mArg * $zArg; $rbA = $dA * [math]::Cos($alpha) / 2.0
  $ra = $dA / 2.0 + $mArg; $rf = $dA / 2.0 - 1.25 * $mArg
  $rStart = [math]::Max($rbA, $rf)
  $step = 2 * [math]::PI / $zArg
  $psiBase = $s / $dA + $invA
  $X = New-Object System.Collections.ArrayList
  $Y = New-Object System.Collections.ArrayList
  for ($t = 0; $t -lt $zArg; $t++) {
    $c = $step * $t
    for ($i = 0; $i -le 1; $i++) {
      $r = $rStart + ($ra - $rStart) * $i
      $psi = $s / $dA + $invA - (Inv ([math]::Acos($rbA / $r)))
      if ($r -le $rbA) { $psi = $psiBase }
      [void]$X.Add($r * [math]::Cos($c - $psi)); [void]$Y.Add($r * [math]::Sin($c - $psi))
    }
    $psiTop = $s / $dA + $invA - (Inv ([math]::Acos($rbA / $ra)))
    [void]$X.Add($ra * [math]::Cos($c + $psiTop)); [void]$Y.Add($ra * [math]::Sin($c + $psiTop))
    $psi = $s / $dA + $invA - (Inv ([math]::Acos($rbA / $rStart)))
    if ($rStart -le $rbA) { $psi = $psiBase }
    [void]$X.Add($rStart * [math]::Cos($c + $psi)); [void]$Y.Add($rStart * [math]::Sin($c + $psi))
    [void]$X.Add($rf * [math]::Cos($c + $psi)); [void]$Y.Add($rf * [math]::Sin($c + $psi))
  }
  $A = 0.0
  for ($i = 0; $i -lt $X.Count; $i++) {
    $j = ($i + 1) % $X.Count
    $A += $X[$i] * $Y[$j] - $X[$j] * $Y[$i]
  }
  return [math]::Abs($A) / 2.0
}
$a20 = AreaCorrect 20 2.0
$a40 = AreaCorrect 40 2.0
"z=20 area: " + $a20.ToString("F1", $inv) + "  mass w20: " + ($a20 * 20 * 7.85e-6).ToString("F5", $inv)
"z=40 area: " + $a40.ToString("F1", $inv) + "  mass w20 bore20: " + (($a40 - [math]::PI * 100) * 20 * 7.85e-6).ToString("F5", $inv)
"z=20 mass w20 bore15: " + (($a20 - [math]::PI * 56.25) * 20 * 7.85e-6).ToString("F5", $inv)
"tooth s at pitch z=20: " + (2 * 20 * ($s / $d + $invA - (Inv ([math]::Acos($rb / 20.0))))).ToString("F4", $inv)
"tooth s at tip: " + (2 * 22 * ($s / $d + $invA - (Inv ([math]::Acos($rb / 22.0))))).ToString("F4", $inv)