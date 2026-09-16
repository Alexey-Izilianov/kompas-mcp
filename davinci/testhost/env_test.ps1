# Проверка авторизации claude при чистом окружении (имитация панели из КОМПАСа).
# Режим 1: без ANTHROPIC_* — ожидаем "Not logged in" (репро бага).
# Режим 2: только переменные из davinci.json — ожидаем рабочий ответ.
param([string]$mode)
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $env:ComSpec
$psi.Arguments = '/d /s /c "claude.cmd -p --output-format json --model glm-5.3-flash:cloud -p say ok"'
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.WorkingDirectory = "C:\Projects\kompas-test\kompas-mcp\mcp-out"
# чистим окружение до минимума (как у процесса, выросшего из КОМПАСа)
foreach ($k in @('ANTHROPIC_BASE_URL','ANTHROPIC_AUTH_TOKEN','ANTHROPIC_API_KEY',
                 'ANTHROPIC_MODEL','ANTHROPIC_DEFAULT_OPUS_MODEL',
                 'ANTHROPIC_DEFAULT_SONNET_MODEL','ANTHROPIC_DEFAULT_HAIKU_MODEL')) {
  $psi.EnvironmentVariables.Remove($k) | Out-Null
}
if ($mode -eq 'withenv') {
  $psi.EnvironmentVariables['ANTHROPIC_BASE_URL'] = 'http://127.0.0.1:11434'
  $psi.EnvironmentVariables['ANTHROPIC_AUTH_TOKEN'] = 'ollama'
  $psi.EnvironmentVariables['ANTHROPIC_DEFAULT_OPUS_MODEL'] = 'glm-5.3-flash:cloud'
  $psi.EnvironmentVariables['ANTHROPIC_DEFAULT_SONNET_MODEL'] = 'glm-5.3-flash:cloud'
  $psi.EnvironmentVariables['ANTHROPIC_DEFAULT_HAIKU_MODEL'] = 'glm-5.3-flash:cloud'
}
$p = [System.Diagnostics.Process]::Start($psi)
$out = $p.StandardOutput.ReadToEnd()
$err = $p.StandardError.ReadToEnd()
$p.WaitForExit()
Write-Host ("mode=" + $mode + " exit=" + $p.ExitCode)
$out -split "`n" | Where-Object { $_ -match '"result"|Not logged|is_error' } | ForEach-Object { $_.Substring(0,[Math]::Min(300,$_.Length)) }
if ($err) { Write-Host "stderr:"; Write-Host ($err.Substring(0,[Math]::Min(300,$err.Length))) }