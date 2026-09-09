param([string]$Destination, [string]$Groups = 'speech', [string]$Cache, [string]$CancelFile, [string]$PreviewOutput)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Setup Worker.ps1')
[Console]::OutputEncoding=New-Object Text.UTF8Encoding $false
$OutputEncoding=[Console]::OutputEncoding
if (!$Cache) { $Cache=Join-Path $env:LOCALAPPDATA 'RuneFellowship-SetupDownloads' }
if ($Destination) {
    if (!$Groups) {$Groups='core'}
    $Destination=Resolve-RuneDestination $Destination
    Test-RuneDestination $Destination $PSScriptRoot $Cache
    if (Get-Process RuneVoice,valheim -ErrorAction SilentlyContinue) { throw 'Save and close Rune and Valheim before installing or updating.' }
    $catalog=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'online-downloads.json') -Raw | ConvertFrom-Json
    $entry=$catalog.downloads | Where-Object { $_.target -eq 'runtime/python' }
    New-Item -ItemType Directory -Path $Cache -Force | Out-Null
    $archive=Join-Path $Cache $entry.sha256
    if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) {
        Write-Output 'Downloading the small Python runtime from python.org…'
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        $partial=$archive+'.part'
        $client=New-Object Net.WebClient
        try { $client.DownloadFile($entry.url,$partial) } finally { $client.Dispose() }
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'The Python download did not pass verification. Please retry.' }
        Move-Item -LiteralPath $partial -Destination $archive -Force
    }
    if ($CancelFile -and (Test-Path -LiteralPath $CancelFile)) { throw 'Cancelled. Downloads are kept for your next attempt.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $bootstrap=Join-Path $Cache ('python-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $bootstrap | Out-Null
    $zip=[IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($file in $zip.Entries) {
            $target=[IO.Path]::GetFullPath((Join-Path $bootstrap $file.FullName))
            if (!$target.StartsWith($bootstrap+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe runtime archive path.' }
            if (!$file.Name) { continue }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($file,$target,$true)
        }
    } finally { $zip.Dispose() }
    # Include this installer directory explicitly; ignore global and user packages.
    @('python312.zip','.', $PSScriptRoot,'import site') | Set-Content -LiteralPath (Join-Path $bootstrap 'python312._pth')
    $stop=Join-Path $Destination 'Stop Rune services.ps1'
    if (Test-Path -LiteralPath $stop) { & $stop }
    $arguments=@('-I','-X','utf8',(Join-Path $PSScriptRoot 'online_installer.py'),'--source',$PSScriptRoot,'--destination',$Destination,'--cache',$Cache,'--groups',$Groups)
    if ($CancelFile) { $arguments+=@('--cancel',$CancelFile) }
    & (Join-Path $bootstrap 'python.exe') @arguments
    $result=$LASTEXITCODE
    # Only this newly created, verified cache child is removed.
    $resolved=[IO.Path]::GetFullPath($bootstrap)
    if ($resolved.StartsWith([IO.Path]::GetFullPath($Cache).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf) -match '^python-[a-f0-9]{32}$') { Remove-Item -LiteralPath $resolved -Recurse -Force }
    exit $result
}
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName System.Windows.Forms
[xml]$xaml=@'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Title="Rune Fellowship · Setup" Width="1000" Height="740" MinWidth="900" MinHeight="700" WindowStartupLocation="CenterScreen" Background="#151914" Foreground="#EEE7D8" FontFamily="Segoe UI">
 <Window.Resources>
  <Style TargetType="Button"><Setter Property="Foreground" Value="#EEE7D8"/><Setter Property="Background" Value="#303A2B"/><Setter Property="BorderBrush" Value="#6C684C"/><Setter Property="Padding" Value="16,10"/><Setter Property="Cursor" Value="Hand"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="6" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="#48553B"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType="CheckBox"><Setter Property="Foreground" Value="#EEE7D8"/><Setter Property="Margin" Value="0,8"/><Setter Property="FontSize" Value="14"/></Style>
 </Window.Resources>
 <Grid>
  <Grid.ColumnDefinitions><ColumnDefinition Width="350"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
  <Grid ClipToBounds="True"><Border Name="Artwork"><Border.Background><ImageBrush Stretch="UniformToFill" Viewbox="0.13,0,0.34,1"/></Border.Background></Border><Border><Border.Background><LinearGradientBrush StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#00151914" Offset="0.45"/><GradientStop Color="#F0151914" Offset="1"/></LinearGradientBrush></Border.Background></Border>
   <StackPanel VerticalAlignment="Bottom" Margin="30,0,24,34"><TextBlock Text="RUNE" FontFamily="Georgia" FontSize="43" Foreground="#E3CE96"/><TextBlock Text="F E L L O W S H I P" FontSize="15" Margin="2,6,0,20"/><TextBlock Text="Your next adventure,&#10;with a companion by your side." FontSize="16" Foreground="#D5D3C4" LineHeight="25"/><TextBlock Text="Unofficial Valheim companion · 0.4.26 beta" FontSize="11" Foreground="#A4A996" Margin="0,22,0,0"/></StackPanel>
  </Grid>
  <ScrollViewer Grid.Column="1" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"><StackPanel Margin="34,30,34,24">
   <TextBlock Text="Make room for Rune" FontFamily="Georgia" FontSize="29"/>
   <TextBlock Text="Choose what you need. Setup downloads it for you." Foreground="#ABB29F" Margin="0,9,0,22"/>
   <TextBlock Text="INSTALL LOCATION" Foreground="#C1AB6E" FontSize="11"/>
   <TextBlock Text="Choosing Desktop or Documents creates a RuneFellowship folder inside it." Foreground="#ABB29F" FontSize="11" TextWrapping="Wrap" Margin="0,4,0,0"/>
   <DockPanel Margin="0,8,0,18"><Button Name="Browse" Content="Browse" DockPanel.Dock="Right" Margin="8,0,0,0" Padding="12,8"/><TextBox Name="Folder" Padding="9" Background="#20261F" Foreground="#EEE7D8" BorderBrush="#515845" VerticalContentAlignment="Center"/></DockPanel>
   <Border Background="#20261F" CornerRadius="8" Padding="16"><StackPanel>
    <TextBlock Text="Voice &amp; local intelligence" FontSize="16" FontWeight="SemiBold" Margin="0,0,0,4"/>
    <CheckBox Name="Speech" Content="Microphone + Kokoro voices" IsChecked="True"/>
    <CheckBox Name="Brain" Content="Local brain · Qwen 3.5 4B"/>
    <CheckBox Name="Turbo" Content="Chatterbox Turbo · expressive English"/>
    <CheckBox Name="Multi" Content="Chatterbox V3 · expressive multilingual"/>
    <TextBlock Text="Optional ChatGPT connection through Codex: your account and usage limits apply. Qwen is not needed for that option. Chatterbox includes voice libraries and Kokoro references. NVIDIA GPU recommended; CPU use is slower." TextWrapping="Wrap" Foreground="#ABB29F" FontSize="12" Margin="0,8,0,0"/>
   </StackPanel></Border>
   <TextBlock Name="Size" Foreground="#D7C58B" Margin="0,16,0,6"/>
   <TextBlock Text="From Microsoft, Python, PyPI, Hugging Face, GitHub and model publishers. Files are checked before installation. No other authors’ game mods are included." TextWrapping="Wrap" Foreground="#ABB29F" FontSize="12"/>
   <TextBlock Name="Status" Text="Ready when you are." TextWrapping="Wrap" MinHeight="42" Margin="0,16,0,6"/>
   <ProgressBar Name="Progress" Height="4" Minimum="0" Maximum="100" Background="#2E3728" Foreground="#B1BA6D" Margin="0,0,0,14"/>
   <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><Button Name="Install" Content="Download &amp; install Rune" FontWeight="SemiBold"/><Button Name="Cancel" Content="Cancel" Grid.Column="1" Margin="10,0,0,0" IsEnabled="False"/></Grid>
   <Button Name="Open" Content="Open installed folder" Visibility="Collapsed" Margin="0,8,0,0"/>
   <Button Name="Log" Content="Open setup log" Visibility="Collapsed" Margin="0,8,0,0"/>
   <TextBlock Name="Notices" Text="Licences &amp; download sources ↗" Foreground="#C1AB6E" Margin="0,12,0,0" Cursor="Hand"/>
  </StackPanel></ScrollViewer>
 </Grid>
</Window>
'@
$window=[Windows.Markup.XamlReader]::Load((New-Object Xml.XmlNodeReader $xaml))
$controls=@{};foreach ($name in @('Artwork','Folder','Browse','Speech','Brain','Turbo','Multi','Size','Status','Progress','Install','Cancel','Open','Log','Notices')) {$controls[$name]=$window.FindName($name)}
$controls.Artwork.Background.ImageSource=New-Object Windows.Media.Imaging.BitmapImage ([uri](Join-Path $PSScriptRoot 'online-assets\rune-installer-original.png'))
$controls.Folder.Text=Join-Path $env:LOCALAPPDATA 'RuneFellowship'
$catalog=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'online-downloads.json') -Raw | ConvertFrom-Json
function Get-SelectedGroups {
    $selected=@('core');if ($controls.Speech.IsChecked) {$selected+='speech'};if ($controls.Brain.IsChecked) {$selected+='brain'}
    if ($controls.Turbo.IsChecked) {$selected+=@('turbo','expressive','speech')};if ($controls.Multi.IsChecked) {$selected+=@('v3','expressive','speech')};return $selected
}
function Update-Size {
    $selected=Get-SelectedGroups;$size=($catalog.downloads | Where-Object {$_.group -in $selected} | Measure-Object bytes -Sum).Sum
    $controls.Size.Text=('Selected downloads · {0:N2} GB' -f ($size/1GB))
}
foreach ($name in @('Speech','Brain','Turbo','Multi')) {$controls[$name].Add_Click({Update-Size})};Update-Size
$controls.Browse.Add_Click({$dialog=New-Object Windows.Forms.FolderBrowserDialog;$dialog.Description='Choose a Rune folder, or Desktop to create RuneFellowship there';if ($dialog.ShowDialog() -eq 'OK') {$controls.Folder.Text=Resolve-RuneDestination $dialog.SelectedPath};$dialog.Dispose()})
$controls.Notices.Add_MouseLeftButtonUp({Start-Process -FilePath (Join-Path $PSScriptRoot 'DOWNLOAD-SOURCES.txt')})
$script:job=$null;$script:cancelPath=$null;$script:installedFolder=$null;$script:jobResult=$null;$script:logPath=$null
$timer=New-Object Windows.Threading.DispatcherTimer;$timer.Interval=[TimeSpan]::FromMilliseconds(350)
$controls.Install.Add_Click({
 try {
    $dest=Resolve-RuneDestination $controls.Folder.Text
    $controls.Folder.Text=$dest
    Test-RuneDestination $dest $PSScriptRoot $Cache
    if (Get-Process RuneVoice,valheim -ErrorAction SilentlyContinue) {throw 'Save and close Rune and Valheim, then retry.'}
    $selected=Get-SelectedGroups | Where-Object {$_ -ne 'expressive'} | Select-Object -Unique
    New-Item -ItemType Directory -Path $Cache -Force | Out-Null
    $script:cancelPath=Join-Path $Cache ('cancel-'+[guid]::NewGuid().ToString('N'))
    $script:installedFolder=$dest
    $script:jobResult=$null;$controls.Open.Visibility='Collapsed';$controls.Log.Visibility='Collapsed'
    $script:logPath=Join-Path $Cache ('setup-'+[guid]::NewGuid().ToString('N')+'.log')
    $script:job=Start-RuneSetupJob -ScriptPath (Join-Path $PSScriptRoot 'Online Setup.ps1') -Destination $dest -Groups ($selected -join ',') -Cache $Cache -CancelFile $script:cancelPath -LogFile $script:logPath
    foreach ($name in @('Install','Folder','Browse','Speech','Brain','Turbo','Multi')) {$controls[$name].IsEnabled=$false}
    $controls.Cancel.IsEnabled=$true;$controls.Progress.IsIndeterminate=$true;$controls.Status.Text='Starting your installation…';$timer.Start()
 } catch {$controls.Status.Text=$_.Exception.Message}
})
$controls.Cancel.Add_Click({if ($script:cancelPath) {Set-Content -LiteralPath $script:cancelPath -Value 'cancel';$controls.Cancel.IsEnabled=$false;$controls.Status.Text='Cancelling after the current step…'}})
$timer.Add_Tick({
 if (!$script:job) {return}
 $messages=@(Receive-Job $script:job -ErrorAction SilentlyContinue -ErrorVariable jobError)
 foreach($message in $messages) {
    if($message -isnot [string] -and $message.RuneSetupResult) {$script:jobResult=$message;$controls.Status.Text=$message.Message}
    elseif(!$script:jobResult) {$controls.Status.Text=[string]$message}
 }
 if ($messages -match 'Finishing installation') {$controls.Cancel.IsEnabled=$false}
 if ($jobError) {$controls.Status.Text=[string]$jobError[-1]}
 if ($script:job.State -in @('Completed','Failed','Stopped')) {
    $timer.Stop();$controls.Progress.IsIndeterminate=$false;$controls.Cancel.IsEnabled=$false
    foreach ($name in @('Install','Folder','Browse','Speech','Brain','Turbo','Multi')) {$controls[$name].IsEnabled=$true}
    if ($script:jobResult -and $script:jobResult.Success) {$controls.Progress.Value=100;$controls.Status.Text=$script:jobResult.Message;$controls.Open.Visibility='Visible'} else {$controls.Install.Content='Retry installation';$controls.Progress.Value=0;if($script:jobResult){$controls.Status.Text=$script:jobResult.Message}else{$controls.Status.Text='Setup stopped unexpectedly. Open the setup log for details.'}}
    if(Test-Path -LiteralPath $script:logPath){$controls.Log.Visibility='Visible'}
    Remove-Job $script:job;$script:job=$null
 }
})
$controls.Open.Add_Click({Start-Process explorer.exe -ArgumentList ('"'+$script:installedFolder+'"')})
$controls.Log.Add_Click({if($script:logPath){Start-Process notepad.exe -ArgumentList ('"'+$script:logPath+'"')}})
$window.Add_Closing({param($sender,$e) if ($script:job) {$e.Cancel=$true;$controls.Status.Text='Use Cancel and wait for setup to stop before closing.'}})
if ($PreviewOutput) {
    $controls.Folder.Text='%LocalAppData%\RuneFellowship'
    $window.Show();$window.UpdateLayout()
    $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap ([int]$window.ActualWidth),([int]$window.ActualHeight),96,96,([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($window);$encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder;$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream=[IO.File]::Create($PreviewOutput);try {$encoder.Save($stream)} finally {$stream.Dispose()};$window.Close();exit 0
}
[void]$window.ShowDialog()
