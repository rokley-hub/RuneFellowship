using System.Windows;
using System.Windows.Threading;
using Wpf = System.Windows.Controls;

namespace Rune.Voice;

public partial class LauncherWindow
{
    private void PerformanceSettings_Click(object sender, RoutedEventArgs e)
        => CreatePerformanceWindow().ShowDialog();
    internal Window CreatePerformanceWindow()
    {
        var root = new Wpf.Grid();
        var window = CreateRuneDialog("Speech & performance", root, 700);
        for (int i = 0; i < 8; i++) root.RowDefinitions.Add(new Wpf.RowDefinition { Height = i == 6 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        void Add(UIElement element, int row) { Wpf.Grid.SetRow(element, row); root.Children.Add(element); }
        Wpf.TextBlock Text(string value, int row, int size = 13) { var label = new Wpf.TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; Add(label, row); return label; }
        Text("Balance voice quality, responsiveness and resource use.", 0);
        var mode = new Wpf.ComboBox { Style = (Style)FindResource(typeof(Wpf.ComboBox)), Margin = new Thickness(0, 0, 0, 10) };
        mode.Items.Add("Automatic fallback · selected voice, fast recovery if needed");
        mode.Items.Add("Expressive · selected voice, no substitution");
        mode.Items.Add("Lightweight · fast CPU voice for English replies");
        mode.SelectedIndex = runtime.ShellPerformanceMode == "expressive" ? 1 : runtime.ShellPerformanceMode == "lightweight" ? 2 : 0;
        Add(mode, 1);
        var keep = new Wpf.CheckBox { Content = "Keep the selected voice ready while Rune is open", IsChecked = runtime.ShellKeepVoiceReady, Margin = new Thickness(0, 5, 0, 14) }; Add(keep, 2);
        var pause = new Wpf.ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        pause.Items.Add("Send after a 0.6-second pause · quicker, may split sentences");
        pause.Items.Add("Send after a 0.9-second pause · balanced");
        pause.Items.Add("Send after a 1.2-second pause · more thinking time");
        pause.SelectedIndex = runtime.ShellSpeechPause <= 600 ? 0 : runtime.ShellSpeechPause >= 1200 ? 2 : 1; Add(pause, 3);
        Text("Recognition and fast voice use the CPU; Chatterbox uses the GPU when available. ChatGPT mode releases Rune’s unused Qwen model. Keeping Chatterbox ready uses video memory.\n\nLightweight uses the companion’s source speaker, with less expression. German and Dutch retain the selected engine. Voice tests always use the selected engine.", 4);
        var readiness = Text("", 5);
        var measurements = Text("", 6);
        var footer = new Wpf.StackPanel { Orientation = Wpf.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var report = new Wpf.Button { Content = "Session report", Style = (Style)FindResource("RuneButton"), Margin = new Thickness(0, 0, 12, 0) }; report.Click += (_, _) => runtime.ShellOpenSessionReport(); footer.Children.Add(report);
        var apply = new Wpf.Button { Content = "Apply", Style = (Style)FindResource("RuneButton") };
        apply.Click += (_, _) => { runtime.ShellSetPerformance(mode.SelectedIndex == 1 ? "expressive" : mode.SelectedIndex == 2 ? "lightweight" : "automatic", keep.IsChecked == true, pause.SelectedIndex == 0 ? 600 : pause.SelectedIndex == 2 ? 1200 : 900); readiness.Text = "Settings applied · " + runtime.ShellVoiceReadiness; };
        footer.Children.Add(apply); Add(footer, 7);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void Refresh() { readiness.Text = runtime.ShellVoiceReadiness; measurements.Text = "Whole-PC load and each speech stage’s latest measurement\n" + runtime.ShellPerformance; }
        timer.Tick += (_, _) => Refresh(); window.Closed += (_, _) => timer.Stop(); Refresh(); timer.Start(); return window;
    }
}
