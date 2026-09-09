param([string]$Output)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Setup Worker.ps1')
$root=Join-Path $PSScriptRoot ('.worker-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$checks=@{}
try {
    $desktop=Join-Path $root 'Desktop';$source=Join-Path $desktop 'New folder';$cache=Join-Path $root 'cache'
    New-Item -ItemType Directory -Path $source,$cache -Force | Out-Null
    $checks.desktopCreatesSubfolder=(Resolve-RuneDestination $desktop @($desktop)) -eq (Join-Path $desktop 'RuneFellowship')
    $selected=Join-Path $desktop 'Rune Fellowship'
    $checks.customFolderWithSpacesRetained=(Resolve-RuneDestination $selected @($desktop)) -eq $selected
    Test-RuneDestination $selected $source $cache
    $checks.separateDesktopFolderAccepted=$true
    try {Test-RuneDestination $source $source $cache;$checks.installerFolderExplained=$false} catch {$checks.installerFolderExplained=$_.Exception.Message -like '*extracted installer folder*'}
    $occupied=Join-Path $root 'occupied';New-Item -ItemType Directory $occupied | Out-Null;Set-Content (Join-Path $occupied 'keep.txt') 'personal'
    try {Test-RuneDestination $occupied $source $cache;$checks.unrelatedFilesProtected=$false} catch {$checks.unrelatedFilesProtected=$_.Exception.Message -like '*other files*'}
    foreach($code in @(0,1)) {
        $fixture=Join-Path $root ('result-'+$code+'.ps1')
        @'
param($Destination,$Groups,$Cache,$CancelFile)
Write-Output 'Preparing fixture'
Write-Output 'Specific destination failure'
'@ | Set-Content $fixture
        Add-Content $fixture ('exit '+$code)
        $log=Join-Path $cache ('test-'+$code+'.log')
        $job=Start-RuneSetupJob $fixture $selected 'core' $cache (Join-Path $cache 'cancel') $log
        $job | Wait-Job | Out-Null
        $messages=@(Receive-Job $job);Remove-Job $job
        $result=$messages | Where-Object {$_ -isnot [string] -and $_.RuneSetupResult} | Select-Object -Last 1
        $checks['exit'+$code+'Reported']=($null -ne $result -and $result.Success -eq ($code -eq 0))
        if($code -eq 1){$checks.failureReasonRetained=$result.Message -eq 'Specific destination failure';$checks.failureLogged=(Get-Content $log -Raw) -like '*Specific destination failure*'}
    }
    $result=@{passed=($checks.Values -notcontains $false);checks=$checks;scope='Windows PowerShell background worker, destination and error-report fixtures; no game or user install changed.'} | ConvertTo-Json -Depth 5
    if($Output){$result | Set-Content $Output};Write-Output $result
    if($checks.Values -contains $false){throw 'Setup worker regression failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    if(!$resolved.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notmatch '^\.worker-test-[a-f0-9]{32}$'){throw 'Invalid fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
