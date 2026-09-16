# Повтор точного спавна claude, как делает ClaudeRunner (без --model).
$mcp = "$env:TEMP\kompas-davinci\mcp-config.json"
$argLine = '/d /s /c ""claude.cmd" --verbose --output-format stream-json --strict-mcp-config --mcp-config "' + $mcp + '" --allowedTools "mcp__kompas-mcp" --permission-prompts none --restricted -p "say ok""'
$p = New-Object System.Diagnostics.Process
$psi = $p.StartInfo
$psi.FileName = $env:ComSpec
$psi.Arguments = $argLine
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.WorkingDirectory = "C:\Projects\kompas-test\kompas-mcp\mcp-out"
$p.Start() | Out-Null
$out = $p.StandardOutput.ReadToEnd()
$err = $p.StandardError.ReadToEnd()
$p.WaitForExit()
Write-Host "--- stdout (первые 4000):"
Write-Host ($out.Substring(0, [Math]::Min(4000, $out.Length)))
Write-Host "--- stderr:"
Write-Host $err
Write-Host "--- exit:" $p.ExitCode