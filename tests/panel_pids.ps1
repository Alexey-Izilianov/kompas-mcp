# List running KompasMcp processes (panel holds the exe lock).
$ps = Get-Process -Name KompasMcp -ErrorAction SilentlyContinue
if ($ps) { $ps | ForEach-Object { Write-Host ("KompasMcp pid: " + $_.Id) } } else { Write-Host "none" }