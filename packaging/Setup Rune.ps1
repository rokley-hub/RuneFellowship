param([string]$Destination, [string[]]$Pack = @(), [string]$PackDirectory, [string]$PreviewOutput)
$ErrorActionPreference = 'Stop'
$python = Join-Path $PSScriptRoot 'runtime\python\python.exe'
$tool = Join-Path $PSScriptRoot 'package_tools.py'
if (!$PackDirectory) { $PackDirectory = Split-Path $PSScriptRoot -Parent }
if ($Destination) {
    if (Get-Process RuneVoice,valheim -ErrorAction SilentlyContinue) { throw 'Save and close Rune and Valheim before installing or updating.' }
    $stop=Join-Path $Destination 'Stop Rune services.ps1'
    if (Test-Path -LiteralPath $stop) { & $stop }
    & $python -I $tool install $PSScriptRoot $Destination
    if ($LASTEXITCODE -ne 0) { throw 'Installation failed; see the preceding message.' }
    $catalog = Get-Content (Join-Path $Destination 'packs.json') -Raw | ConvertFrom-Json
    foreach ($name in $Pack) {
        $entry = $catalog.PSObject.Properties[$name]
        if (!$entry) { throw "Unknown optional pack: $name" }
        $archive = Join-Path $PackDirectory $entry.Value.file
        & $python -I $tool pack $Destination $name $archive
        if ($LASTEXITCODE -ne 0) { throw "Could not install $name. The core app is installed; rerun setup to retry the pack." }
    }
    Write-Output "Ready. Open $Destination\Open Rune.exe"
    exit 0
}
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName System.Windows.Forms
[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Title="Install Rune Fellowship" Width="640" Height="660" ResizeMode="NoResize" WindowStartupLocation="CenterScreen" Background="#171813" Foreground="#EEE4CE" FontFamily="Segoe UI">
 <ScrollViewer VerticalScrollBarVisibility="Auto">
 <StackPanel Margin="28">
  <TextBlock Text="Rune Fellowship" FontSize="28" FontFamily="Georgia" Foreground="#D6B66B"/>
 <TextBlock Text="Installer preview · unofficial Valheim companion" Margin="0,6,0,24" Foreground="#ADA895"/>
  <TextBlock Text="Install folder"/>
  <DockPanel Margin="0,8,0,18"><Button Name="Browse" Content="Browse…" DockPanel.Dock="Right" Width="90" Margin="8,0,0,0"/><TextBox Name="Folder" Padding="8" Background="#292A22" Foreground="#EEE4CE"/></DockPanel>
  <TextBlock Text="Optional packs" FontSize="17"/>
  <CheckBox Name="Speech" Content="Speech models · microphone recognition + Kokoro voices" Foreground="#EEE4CE" Margin="0,6" Visibility="Collapsed"/>
  <TextBlock Text="Place the downloaded pack ZIPs beside the extracted core folder. Grey options are not downloaded yet." TextWrapping="Wrap" Margin="0,6,0,10" Foreground="#ADA895"/>
  <CheckBox Name="Local" Content="Local brain · Qwen + Ollama" Foreground="#EEE4CE" Margin="0,6"/>
  <CheckBox Name="Turbo" Content="Chatterbox Turbo · expressive English" Foreground="#EEE4CE" Margin="0,6"/>
  <CheckBox Name="Multi" Content="Chatterbox V3 · expressive multilingual" Foreground="#EEE4CE" Margin="0,6"/>
  <TextBlock Name="ModelInfo" Text="Kokoro voice and English recognition are included. ChatGPT requires your own sign-in. Updates retain profiles and conversations. No game saves are installed or removed." TextWrapping="Wrap" Margin="0,20,0,12" Foreground="#ADA895"/>
  <TextBlock Name="Status" Text="Choose an empty folder, or your existing Rune beta folder to update." TextWrapping="Wrap" MinHeight="52" Margin="0,0,0,12"/>
  <Button Name="Install" Content="Install Rune" Padding="12" Background="#474D2D" Foreground="#FFF3D7"/>
  <Button Name="OpenFolder" Content="Open installed folder" Visibility="Collapsed" Padding="10" Margin="0,8,0,0" Background="#292A22" Foreground="#FFF3D7"/>
 </StackPanel>
 </ScrollViewer>
</Window>
'@
$window = [Windows.Markup.XamlReader]::Load([System.Xml.XmlNodeReader]::new($xaml))
$window.MaxHeight=[System.Windows.SystemParameters]::WorkArea.Height-32
$folder = $window.FindName('Folder'); $folder.Text = Join-Path $env:LOCALAPPDATA 'RuneFellowship'
$status = $window.FindName('Status'); $install = $window.FindName('Install')
$openFolder = $window.FindName('OpenFolder'); $script:installedFolder = $null
$openFolder.Add_Click({ if ($script:installedFolder) { Start-Process explorer.exe -ArgumentList ('"'+$script:installedFolder+'"') } })
$choices = @($window.FindName('Local'),$window.FindName('Turbo'),$window.FindName('Multi'))
$catalog = Get-Content (Join-Path $PSScriptRoot 'packs.json') -Raw | ConvertFrom-Json
$speechChoice=$window.FindName('Speech')
if ($catalog.PSObject.Properties['speech-core']) {
    $speechChoice.Visibility='Visible'
    $speechChoice.IsEnabled=Test-Path -LiteralPath (Join-Path $PackDirectory $catalog.'speech-core'.file)
    $speechChoice.IsChecked=$speechChoice.IsEnabled
    $speechChoice.Content += ' · '+[math]::Round($catalog.'speech-core'.bytes/1MB)+' MB'
    $window.FindName('ModelInfo').Text='Compact installer: speech models are a separate download. Put the speech ZIP beside this extracted installer folder to enable it, or rerun setup later. Without it, use typed conversation after choosing a brain. ChatGPT requires your own sign-in. Existing profiles and conversations are retained.'
}
$ids = @('local-brain','chatterbox-turbo','chatterbox-v3')
for ($i=0; $i -lt 3; $i++) {
    $entry = $catalog.PSObject.Properties[$ids[$i]]
    $available = $entry -and (Test-Path -LiteralPath (Join-Path $PackDirectory $entry.Value.file))
    if ($i -gt 0) { $available = $available -and (Test-Path -LiteralPath (Join-Path $PackDirectory $catalog.'expressive-runtime'.file)) }
    $choices[$i].IsEnabled = [bool]$available
    if ($entry) { $choices[$i].Content += ' · ' + [math]::Round($entry.Value.bytes / 1GB,1) + ' GB' }
}
$choices[0].IsChecked = $choices[0].IsEnabled
$window.FindName('Browse').Add_Click({
    $picker = [System.Windows.Forms.FolderBrowserDialog]::new()
    if ($picker.ShowDialog() -eq 'OK') { $folder.Text = Join-Path $picker.SelectedPath 'RuneFellowship' }
    $picker.Dispose()
})
$script:setupJob = $null
$timer = [System.Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds(500)
$timer.Add_Tick({
    if (!$script:setupJob) { return }
    $messages = Receive-Job $script:setupJob -ErrorAction Continue 2>&1
    if ($messages) { $status.Text = ($messages | Select-Object -Last 1).ToString() }
    if ($script:setupJob.State -in @('Completed','Failed','Stopped')) {
        $install.IsEnabled=$true
        if ($script:setupJob.State -eq 'Completed') {
            $status.Text='Installed. Open Rune.exe in your chosen folder, then use Mods to install the game requirements.'
            $script:installedFolder=$script:installDestination;$openFolder.Visibility='Visible'
        } else { $status.Text='Setup stopped. '+$script:setupJob.ChildJobs[0].JobStateInfo.Reason.Message }
        $folder.IsEnabled=$true;$window.FindName('Browse').IsEnabled=$true
        Remove-Job $script:setupJob;$script:setupJob=$null;$timer.Stop()
    }
})
$install.Add_Click({
    if ([string]::IsNullOrWhiteSpace($folder.Text)) { $status.Text='Choose an installation folder first.';return }
    $selected = @();for ($i=0;$i -lt 3;$i++) { if ($choices[$i].IsChecked) { $selected += $ids[$i] } }
    if ($speechChoice.IsChecked) { $selected += 'speech-core' }
    if ($selected -contains 'chatterbox-turbo' -or $selected -contains 'chatterbox-v3') { $selected = @('expressive-runtime') + $selected }
    $script:installDestination=$folder.Text.Trim()
    $install.IsEnabled=$false;$folder.IsEnabled=$false;$window.FindName('Browse').IsEnabled=$false;$openFolder.Visibility='Collapsed';$status.Text='Verifying and installing. This may take several minutes…'
    $script:setupJob = Start-Job -ScriptBlock { param($scriptPath,$dest,$packs,$packRoot) $ErrorActionPreference='Stop'; & $scriptPath -Destination $dest -Pack $packs -PackDirectory $packRoot } -ArgumentList (Join-Path $PSScriptRoot 'Setup Rune.ps1'),$script:installDestination,$selected,$PackDirectory
    $timer.Start()
})
$window.Add_Closing({ param($sender,$eventArgs) if ($script:setupJob -and $script:setupJob.State -eq 'Running') { $eventArgs.Cancel=$true;$status.Text='Please wait for installation to finish.' } })
if ($PreviewOutput) {
    $window.WindowStartupLocation='Manual';$window.Left=-20000;$window.Top=-20000;$window.ShowInTaskbar=$false
    $window.Show();$window.UpdateLayout()
    $bitmap=[System.Windows.Media.Imaging.RenderTargetBitmap]::new([int]$window.ActualWidth,[int]$window.ActualHeight,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($window);$encoder=[System.Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream=[IO.File]::Create($PreviewOutput);try{$encoder.Save($stream)}finally{$stream.Dispose()};$window.Close();return
}
$window.ShowDialog() | Out-Null
