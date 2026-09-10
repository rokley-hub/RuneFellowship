param([Parameter(Mandatory=$true)][string]$FixtureRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Update Recovery.ps1')
$root=Join-Path ([IO.Path]::GetFullPath($FixtureRoot)) ('update-recovery-'+[guid]::NewGuid().ToString('N'))
$backup=Join-Path $root ('release-backups/update-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $root 'app'),(Join-Path $backup 'app'),(Join-Path $root 'bridge') -Force | Out-Null
'old app' | Set-Content -LiteralPath (Join-Path $backup 'app/RuneVoice.dll')
'partial new app' | Set-Content -LiteralPath (Join-Path $root 'app/RuneVoice.dll')
'new file' | Set-Content -LiteralPath (Join-Path $root 'app/new.dll')
'keep preferences' | Set-Content -LiteralPath (Join-Path $root 'bridge/preferences.json')
$hash=(Get-FileHash -LiteralPath (Join-Path $backup 'app/RuneVoice.dll')).Hash
@(@{Path='app/RuneVoice.dll';Existed=$true;Previous=$hash},@{Path='app/new.dll';Existed=$false;Previous=$null}) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'update-transaction.json')
Restore-InterruptedRuneUpdate $root
if((Get-Content -LiteralPath (Join-Path $root 'app/RuneVoice.dll')) -ne 'old app' -or (Test-Path -LiteralPath (Join-Path $root 'app/new.dll')) -or (Get-Content -LiteralPath (Join-Path $root 'bridge/preferences.json')) -ne 'keep preferences'){throw 'Interrupted update recovery failed'}
Restore-InterruptedRuneUpdate $root
'PASS interrupted update restores old files, removes only newly installed files, preserves preferences, and is safe to repeat.'
