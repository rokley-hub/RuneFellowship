function Resolve-RuneDestination {
    param([string]$Destination, [string[]]$ParentFolders = @([Environment]::GetFolderPath('DesktopDirectory'),[Environment]::GetFolderPath('MyDocuments'),[Environment]::GetFolderPath('UserProfile')))
    if ([string]::IsNullOrWhiteSpace($Destination)) {throw 'Choose an install folder.'}
    $full=[IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($Destination.Trim()))
    foreach ($parent in $ParentFolders) {
        if ($parent -and $full.TrimEnd('\','/') -eq [IO.Path]::GetFullPath($parent).TrimEnd('\','/')) {return Join-Path $full 'RuneFellowship'}
    }
    return $full
}
function Test-RuneDestination {
    param([string]$Destination,[string]$Source,[string]$Cache)
    $target=[IO.Path]::GetFullPath($Destination).TrimEnd('\','/')
    $installer=[IO.Path]::GetFullPath($Source).TrimEnd('\','/')
    if($target -eq $installer) {throw 'This is the extracted installer folder. Choose a separate install folder, such as Desktop\RuneFellowship.'}
    foreach ($folder in @($Source,$Cache)) {
        $other=[IO.Path]::GetFullPath($folder).TrimEnd('\','/')
        if ($target -eq $other -or $target.StartsWith($other+'\',[StringComparison]::OrdinalIgnoreCase) -or $other.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)) {throw 'Choose a separate Rune install folder, outside the extracted installer and download cache.'}
    }
    if (Test-Path -LiteralPath $target -PathType Leaf) {throw 'The selected location is a file. Choose a folder instead.'}
    if ((Test-Path -LiteralPath $target) -and !(Test-Path -LiteralPath (Join-Path $target 'release.json')) -and @(Get-ChildItem -LiteralPath $target -Force).Count) {throw 'This folder already contains other files. Choose an empty RuneFellowship subfolder or an existing Rune installation.'}
}
function Start-RuneSetupJob {
    param([string]$ScriptPath,[string]$Destination,[string]$Groups,[string]$Cache,[string]$CancelFile,[string]$LogFile)
    Start-Job -ScriptBlock {
        param($scriptPath,$dest,$groups,$cachePath,$cancelPath,$logFile)
        $ErrorActionPreference='Stop'
        $lines=New-Object 'Collections.Generic.List[string]'
        $encoding=New-Object Text.UTF8Encoding $true
        [IO.File]::WriteAllText($logFile,('Rune setup log - '+[DateTime]::Now.ToString('s')+[Environment]::NewLine),$encoding)
        try {
            $global:LASTEXITCODE=0
            & $scriptPath -Destination $dest -Groups $groups -Cache $cachePath -CancelFile $cancelPath 2>&1 | ForEach-Object {
                $line=[string]$_;$lines.Add($line)
                [IO.File]::AppendAllText($logFile,$line+[Environment]::NewLine,$encoding)
                Write-Output $line
            }
            if ($LASTEXITCODE -ne 0) {
                $reason=if($lines.Count){$lines[$lines.Count-1]}else{'Setup ended before reporting a result. Open the setup log for details.'}
                throw $reason
            }
            [pscustomobject]@{RuneSetupResult=$true;Success=$true;Message='Rune is installed. Open the folder to start your adventure.';LogFile=$logFile}
        } catch {
            $reason=$_.Exception.Message
            [IO.File]::AppendAllText($logFile,('ERROR: '+$reason+[Environment]::NewLine+($_ | Out-String)+[Environment]::NewLine),$encoding)
            [pscustomobject]@{RuneSetupResult=$true;Success=$false;Message=$reason;LogFile=$logFile}
        }
    } -ArgumentList $ScriptPath,$Destination,$Groups,$Cache,$CancelFile,$LogFile
}
