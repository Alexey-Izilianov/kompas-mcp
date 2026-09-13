# List running KOMPAS processes.
$ps = Get-Process -Name KOMPAS -ErrorAction SilentlyContinue
if ($ps) { $ps | ForEach-Object { Write-Host ("KOMPAS pid: " + $_.Id) } } else { Write-Host "none" }