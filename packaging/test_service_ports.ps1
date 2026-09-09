param([string]$Output)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Service Ports.ps1')
$root=Join-Path $PSScriptRoot 'port-fixture'
$base=Select-RunePortBase $root 12439
$listener=New-Object Net.Sockets.TcpListener ([Net.IPAddress]::Loopback),($base+2)
$listener.Server.ExclusiveAddressUse=$true;$listener.Start()
try {
    $selected=Select-RunePortBase $root $base
    $checks=@{occupiedPortDetected=!(Test-RunePortRange $base);fallbackChosen=$selected -ne $base;fallbackRangeFree=(Test-RunePortRange $selected);unrelatedListenerPreserved=$listener.Server.IsBound;instanceKeyIgnoresCase=(Get-RuneInstanceKey $root) -eq (Get-RuneInstanceKey $root.ToUpperInvariant())}
    $result=@{passed=$checks.Values -notcontains $false;checks=$checks;note='Real loopback listener fixture; no game or other service was stopped.'} | ConvertTo-Json -Depth 4
    if($Output){$result | Set-Content -LiteralPath $Output};Write-Output $result
    if($checks.Values -contains $false){throw 'Port regression failed'}
} finally {$listener.Stop()}
