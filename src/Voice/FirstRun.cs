using System.Windows;
using Wpf = System.Windows.Controls;

namespace Rune.Voice;

public partial class LauncherWindow
{
    private bool firstRunShown;
    private void ShowFirstRunOnce()
    {
        string marker = Path.Combine(runtime.ShellBridgeFolder, "welcome-seen.txt");
        if (firstRunShown || File.Exists(marker)) return;
        firstRunShown = true;
        CreateWelcomeWindow(marker).ShowDialog();
    }

    internal Window CreateWelcomeWindow(string? marker = null)
    {
        var panel = new Wpf.StackPanel();
        panel.Children.Add(DialogText("Welcome to Rune Fellowship · testing beta", true));
        panel.Children.Add(DialogText("An unofficial Valheim companion. Start in a separate test world until you are comfortable with its current limits."));
        Window? dialog = null;
        void Step(string heading, string text, string button, Action action) {
            panel.Children.Add(new Wpf.TextBlock { Text = heading, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 5) });
            panel.Children.Add(DialogText(text, true));
            var control = DialogButton(button); control.HorizontalAlignment = System.Windows.HorizontalAlignment.Left; control.Margin = new Thickness(0, 8, 0, 0);
            control.Click += (_, _) => { dialog!.Close(); action(); }; panel.Children.Add(control);
        }
        Step("1 · Your game and mods", "Locate your installed Valheim, then create a profile in Mods. Rune downloads BepInEx, Jötunn, PlanBuild and their dependencies automatically. For an existing profile, choose Install Rune requirements.", "Open Mods", () => ShowPage("Mods"));
        Step("2 · Choose a brain", "Local mode needs the Local brain pack. ChatGPT needs your own sign-in and available account usage. Audio remains local in both modes; ChatGPT receives conversation text and relevant game context.", "Open Settings", () => ShowPage("Settings"));
        Step("3 · Check your voice and controls", "Select your microphone and headphones separately in Settings. Bind your microphone key, choose a voice, and use Test voice before entering the game. Install an expressive voice pack to use Chatterbox.", "Open Companions", () => ShowPage("Companions"));
        var done = DialogButton("I have reviewed setup"); done.Margin = new Thickness(0, 22, 0, 0);
        done.Click += (_, _) => { if (marker != null) { Directory.CreateDirectory(Path.GetDirectoryName(marker)!); File.WriteAllText(marker, DateTime.UtcNow.ToString("O")); } dialog!.Close(); };
        panel.Children.Add(done);
        dialog = CreateRuneDialog("Start your fellowship", panel, 640); return dialog;
    }
}
