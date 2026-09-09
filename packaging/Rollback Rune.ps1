$ErrorActionPreference='Stop'
if (Get-Process RuneVoice,valheim -ErrorAction SilentlyContinue) { throw 'Save and close Rune and Valheim first.' }
& (Join-Path $PSScriptRoot 'Stop Rune services.ps1')
$runner=Join-Path $PSScriptRoot ('release-backups\rollback-runner-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runner | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'runtime\python') -Destination $runner -Recurse
& (Join-Path $runner 'python\python.exe') -I (Join-Path $PSScriptRoot 'package_tools.py') rollback $PSScriptRoot
if ($LASTEXITCODE -ne 0) { throw 'Rollback stopped. Your data was retained.' }
Read-Host 'Press Enter to close'
