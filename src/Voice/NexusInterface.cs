using System.Diagnostics;
using System.Windows;
using Wpf = System.Windows.Controls;

namespace Rune.Voice;
public partial class LauncherWindow
{
    private void BrowseNexus_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(NexusMods.BrowseUrl) {UseShellExecute=true});
        ModNoticeText.Text="Browse Nexus in your browser, choose Manual download, then Import Nexus ZIP here. ZIP plugins are supported; author dependencies must be installed separately.";
    }
    private async void ImportNexus_Click(object sender, RoutedEventArgs e)
    {
        if (ModsProfileCombo.SelectedItem is not ProfileChoice profile) {ModNoticeText.Text="Create or select a profile first.";return;}
        var picker=new Microsoft.Win32.OpenFileDialog {Title="Choose the ZIP downloaded from Nexus Mods",Filter="Mod archive (*.zip)|*.zip"};
        if(picker.ShowDialog(this)!=true)return;
        var body=new Wpf.StackPanel();
        body.Children.Add(new Wpf.TextBlock {Text="Import into "+profile.Label+". Read the author's requirements first; Nexus dependencies and updates are checked on the website. Reimport a newer ZIP using the same page address to update.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        Wpf.TextBox Field(string label,string value) {
            body.Children.Add(new Wpf.TextBlock {Text=label,Margin=new Thickness(0,10,0,5)});
            var box=new Wpf.TextBox {Text=value};body.Children.Add(box);return box;
        }
        var existing=selectedMod?.Installed;
        bool updating=existing!=null&&NexusMods.IsNexus(existing.Id);
        var page=Field("Nexus Valheim mod page",updating?NexusMods.Page(existing!.Id):"");
        var name=Field("Mod name",updating?existing!.Name:Path.GetFileNameWithoutExtension(picker.FileName));
        var version=Field("Downloaded version","");
        var error=new Wpf.TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};body.Children.Add(error);
        var ok=new Wpf.Button {Content="Import into "+profile.Label,Style=(Style)FindResource("RuneButton")};body.Children.Add(ok);
        var dialog=CreateRuneDialog("Import Nexus mod",body,600);
        ok.Click+=(_,_)=>{try { _=NexusMods.IdFromUrl(page.Text);if(string.IsNullOrWhiteSpace(name.Text)||string.IsNullOrWhiteSpace(version.Text))throw new IOException("Enter a name and version.");dialog.DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}};
        if(dialog.ShowDialog()!=true)return;
        ModsPage.IsEnabled=false;ModNoticeText.Text="Checking and importing "+name.Text+" into "+profile.Label+"…";
        try {
            string url=page.Text, modName=name.Text, modVersion=version.Text;
            await Task.Run(()=>OwnedMods.InstallNexus(profile.Path,picker.FileName,url,modName,modVersion));
            SetModsMode(false);RefreshModsList();
            ModNoticeText.Text=modName+" imported into "+profile.Label+". Check its Nexus page for dependencies and updates. Existing settings and enabled/disabled state are kept on updates.";
        }catch(Exception ex){ModNoticeText.Text="Import stopped: "+ex.Message;}finally{ModsPage.IsEnabled=true;}
    }
}
