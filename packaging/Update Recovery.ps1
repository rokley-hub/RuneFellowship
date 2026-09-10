# Called under the installation's startup mutex, before application/services start.
function Restore-InterruptedRuneUpdate {
    param([string]$Root)
    $rootFull=[IO.Path]::GetFullPath($Root).TrimEnd('\')
    function Safe-UpdatePath([string]$Base,[string]$Relative) {
        $parts=$Relative.Replace('\','/').Split('/')
        foreach($part in $parts) {if(!$part -or $part -in @('.','..') -or $part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or $part.EndsWith('.') -or $part.EndsWith(' ')){throw 'Invalid update recovery path.'}}
        $full=[IO.Path]::GetFullPath((Join-Path $Base $Relative))
        if(!$full.StartsWith($Base.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Update recovery path escapes its folder.'}
        $check=$full
        while($check){if((Test-Path -LiteralPath $check) -and ((Get-Item -LiteralPath $check -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked update recovery paths are unsupported.'};$check=[IO.Path]::GetDirectoryName($check)}
        return $full
    }
    $backups=Join-Path $rootFull 'release-backups'
    if(!(Test-Path -LiteralPath $backups)){return}
    foreach($folder in Get-ChildItem -LiteralPath $backups -Directory | Where-Object {$_.Name -match '^update-[a-f0-9]{32}$'} | Sort-Object CreationTime -Descending) {
        $record=Safe-UpdatePath $folder.FullName 'update-transaction.json'
        if(!(Test-Path -LiteralPath $record) -or (Test-Path -LiteralPath (Join-Path $folder.FullName 'COMPLETED.txt')) -or (Test-Path -LiteralPath (Join-Path $folder.FullName 'ROLLED-BACK.txt'))){continue}
        if(Get-Process valheim,RuneVoice -ErrorAction SilentlyContinue){throw 'Close Valheim and Rune before recovering the interrupted update.'}
        [array]$rows=Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
        foreach($row in $rows) {
            $null=Safe-UpdatePath $rootFull $row.Path
            if($row.Existed){$old=Safe-UpdatePath $folder.FullName $row.Path;if(!(Test-Path -LiteralPath $old) -or (Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -ne $row.Previous){throw 'An update backup is damaged. Keep this folder and reinstall Rune to repair the application.'}}
        }
        [array]::Reverse($rows)
        foreach($row in $rows) {
            $destination=Safe-UpdatePath $rootFull $row.Path
            if($row.Existed){Copy-Item -LiteralPath (Safe-UpdatePath $folder.FullName $row.Path) -Destination $destination -Force}
            elseif(Test-Path -LiteralPath $destination){Remove-Item -LiteralPath $destination -Force}
        }
        'Restored an interrupted Rune update at startup.' | Set-Content -LiteralPath (Join-Path $folder.FullName 'ROLLED-BACK.txt')
    }
}
