$mcp = "$env:TEMP\kompas-davinci\mcp-config.json"
$sys = 'Ты — копилот Давинчи. Отвечай кратко.'
$argLine = '/d /s /c ""claude.cmd" --verbose --output-format stream-json --strict-mcp-config --mcp-config "' + $mcp + '" --allowedTools "mcp__kompas-mcp" --permission-prompts none --restricted --append-system-prompt "' + $sys + '""'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $env:ComSpec
$psi.Arguments = $argLine
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.WorkingDirectory = "C:\Projects\kompas-test\kompas-mcp\mcp-out"
$p = [System.Diagnostics.Process]::Start($psi)
[byte[]]$b = [System.Text.Encoding]::UTF8.GetBytes('say ok')
$p.StandardInput.BaseStream.Write($b,0,$b.Length)
$p.StandardInput.Close()
$out = $p.StandardOutput.ReadToEnd()
$err = $p.StandardError.ReadToEnd()
$p.WaitForExit()
$out -split "`n" | Where-Object { $_ -match '"result"|synthetic|error' } | ForEach-Object { $_.Substring(0,[Math]::Min(700,$_.Length)) }
Write-Host "--- stderr:"; Write-Host $err
