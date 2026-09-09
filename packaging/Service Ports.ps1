function Test-RunePortRange {
    param([int]$BasePort)
    $listeners=New-Object 'Collections.Generic.List[Net.Sockets.TcpListener]'
    try {
        foreach($offset in @(0,2,3)) {
            $listener=New-Object Net.Sockets.TcpListener ([Net.IPAddress]::Loopback),($BasePort+$offset)
            $listener.Server.ExclusiveAddressUse=$true
            $listeners.Add($listener);$listener.Start()
        }
        return $true
    } catch {return $false} finally {foreach($listener in $listeners){$listener.Stop()}}
}
function Get-RuneInstanceKey {
    param([string]$Root)
    $hash=[Security.Cryptography.SHA256]::Create()
    try {return ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($Root).ToLowerInvariant())))).Replace('-','')} finally {$hash.Dispose()}
}
function Select-RunePortBase {
    param([string]$Root,[int]$Preferred=11439)
    if(Test-RunePortRange $Preferred){return $Preferred}
    $key=Get-RuneInstanceKey $Root
    $first=20000+([Convert]::ToInt32($key.Substring(0,4),16)%3000)*4
    foreach($index in 0..63) {
        $candidate=$first+($index*4)
        if(Test-RunePortRange $candidate){return $candidate}
    }
    throw 'Rune could not find three free local service ports. Close another Rune copy and retry.'
}
