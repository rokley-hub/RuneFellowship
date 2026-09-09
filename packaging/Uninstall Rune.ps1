$ErrorActionPreference='Stop'
if (Get-Process RuneVoice,valheim -ErrorAction SilentlyContinue) { throw 'Close Rune and Valheim before removing application files.' }
& (Join-Path $PSScriptRoot 'Stop Rune services.ps1')
Add-Type -AssemblyName PresentationFramework
if ([System.Windows.MessageBox]::Show('Remove Rune core files? Your profiles, conversations, downloaded packs and game saves will remain.','Uninstall Rune','YesNo') -ne 'Yes') { exit }
$root=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$manifest=Get-Content -LiteralPath (Join-Path $root 'files.json') -Raw | ConvertFrom-Json
foreach ($file in $manifest.files) {
    $target=[IO.Path]::GetFullPath((Join-Path $root $file.path))
    if (!$target.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path.' }
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        $parent=Get-Item -LiteralPath (Split-Path $target -Parent)
        while ($parent.FullName.Length -ge $root.Length) { if ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked folders are unsupported.' };$parent=$parent.Parent }
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $file.sha256) { Remove-Item -LiteralPath $target }
    }
}
[System.Windows.MessageBox]::Show('Core files removed. Your data and optional model packs remain in the Rune folder.','Rune') | Out-Null
