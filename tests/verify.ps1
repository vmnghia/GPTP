# Runs the extended selection's host test, then builds the plugin (Debug).
# Exit code 0 only if both succeed. The build log goes to tests\out\build-plugin.log.
$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
& "$PSScriptRoot\selection_ext_test.bat" 2>$null
if ($LASTEXITCODE -ne 0) { Write-Output "host test FAILED"; exit 1 }
New-Item -ItemType Directory -Force "$PSScriptRoot\out" | Out-Null
$log = "$PSScriptRoot\out\build-plugin.log"
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "$root\GPTP\GPTP.sln" /p:Configuration=Debug /p:Platform=Win32 /nologo /v:minimal > $log
$code = $LASTEXITCODE
Select-String -Path $log -Pattern ": error |: warning C\d+.*(selection_ext|sel_)" | ForEach-Object { $_.Line }
if ($code -ne 0) { Write-Output "plugin build FAILED"; exit 1 }
Write-Output "plugin build succeeded"
exit 0
