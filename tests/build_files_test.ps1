$ErrorActionPreference = "Stop"
$dir = $PSScriptRoot
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = Join-Path $dir "files_test.exe"
$sources = @(
  (Join-Path $dir "..\src\davinci\ClaudeRunner.cs"),
  (Join-Path $dir "..\src\davinci\StreamJson.cs"),
  (Join-Path $dir "..\src\json\Json.cs"),
  (Join-Path $dir "..\src\Log.cs"),
  (Join-Path $dir "files_test.cs")
)
& $csc -nologo -codepage:65001 -target:exe -out:$out -optimize+ $sources
if ($LASTEXITCODE -ne 0) { throw "csc failed: $LASTEXITCODE" }
Write-Host "BUILD OK: $out"