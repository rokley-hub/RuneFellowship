using System.Diagnostics;
using System.Windows;
using Wpf = System.Windows.Controls;
namespace Rune.Voice;

public partial class LauncherWindow
{
    private bool UseNexus => runtime.ShellModSource == "nexus";
    private void OpenModCatalogue() => Process.Start(new ProcessStartInfo(ModSources.Browse(runtime.ShellModSource)) { UseShellExecute = true });
    private void ModSourceCombo_SelectionChanged(object sender, Wpf.SelectionChangedEventArgs e)
    {
        if (loadingSettings || !IsLoaded) return;
        runtime.ShellSetModSource(ModSourceCombo.SelectedIndex == 1 ? "nexus" : "thunderstore");
        RefreshSettings(); SetModsMode(false);
    }
    private void OpenPreferredModPage(string id, string name)
    {
        string? page = runtime.ShellModPage(id);
        if (page == null) {
            string provider = ModSources.Label(runtime.ShellModSource);
            var body = new Wpf.StackPanel();
            body.Children.Add(new Wpf.TextBlock { Text = "Rune does not know a matching " + provider + " page for " + name + ". Some mods are available on only one site. Browse the selected site and paste the matching Valheim mod page below to remember it.", TextWrapping = TextWrapping.Wrap });
            var browse = new Wpf.Button { Content = "Browse " + provider, Style = (Style)FindResource("RuneButton"), Margin = new Thickness(0,12,0,12) };
            browse.Click += (_,_) => OpenModCatalogue(); body.Children.Add(browse);
            var input = new Wpf.TextBox { ToolTip = "Mod page address" }; body.Children.Add(input);
            var error = new Wpf.TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,10) }; body.Children.Add(error);
            var save = new Wpf.Button { Content = "Save link and open", Style = (Style)FindResource("RuneButton") }; body.Children.Add(save);
            var dialog = CreateRuneDialog("Link mod page",body,560);
            save.Click += (_,_) => { try { runtime.ShellSetModPage(id,input.Text.Trim()); page = input.Text.Trim(); dialog.DialogResult = true; } catch(Exception ex) { error.Text = ex.Message; } };
            if (dialog.ShowDialog() != true) return;
        }
        Process.Start(new ProcessStartInfo(page!) { UseShellExecute = true });
    }
    private void RequirementsCredits_Click(object sender, RoutedEventArgs e)
    {
        var body = new Wpf.StackPanel();
        body.Children.Add(new Wpf.TextBlock { Text = "These are independent projects by their respective authors. Rune is not endorsed by them. Setup downloads their original Thunderstore packages and keeps the included license files. Links below use your chosen mod source.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) });
        foreach (var mod in new[] {
            ("denikson-BepInExPack_Valheim", "BepInEx · BepInEx team; pack by denikson"),
            ("ValheimModding-Jotunn", "Jötunn · Jötunn / Valheim Modding team"),
            ("MathiasDecrock-PlanBuild", "PlanBuild · contributors; package by MathiasDecrock"),
            ("ValheimModding-HookGenPatcher", "HookGenPatcher · Valheim Modding") }) {
            var button = new Wpf.Button { Content = mod.Item2 + " ↗", Style = (Style)FindResource("RuneButton"), Margin = new Thickness(0,0,0,8) };
            button.Click += (_,_) => OpenPreferredModPage(mod.Item1,mod.Item2); body.Children.Add(button);
        }
        CreateRuneDialog("Third-party mods · " + ModSources.Label(runtime.ShellModSource),body,680).ShowDialog();
    }
    private bool ConfirmRequiredModSource() => ShowRuneMessage("Set up BepInEx, Jötunn, PlanBuild and their dependencies from the original Thunderstore packages? Your browsing and mod-page preference stays " + ModSources.Label(runtime.ShellModSource) + ". Included licenses are kept; these projects belong to their authors.", "Set up required mods", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;
}
