$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'runtime\python\python.exe') -I (Join-Path $PSScriptRoot 'package_tools.py') verify $PSScriptRoot
if ($LASTEXITCODE -ne 0) { throw 'Verification failed. Reinstall from the original trusted download.' }
Write-Output 'Core files verified. This checks integrity, not publisher identity.'
Read-Host 'Press Enter to close'
