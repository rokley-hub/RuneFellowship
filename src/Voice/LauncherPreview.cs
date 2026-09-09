using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Rune.Voice;

internal static class LauncherPreview
{
    internal static void Save(string output, string page = "Play", double width = 1440, double height = 840, string? bridgeOverride = null)
    {
        ApplicationConfiguration.Initialize();
        string bridge = string.IsNullOrWhiteSpace(bridgeOverride) ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "wpf-preview-bridge") : Path.GetFullPath(bridgeOverride);
        Directory.CreateDirectory(bridge);
        using var runtime = new RuneWindow(bridge) { Opacity = 0, ShowInTaskbar = false };
        runtime.Show();
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        var window = new LauncherWindow(runtime) {
            Width = width, Height = height, Left = -20000, Top = -20000,
            ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = System.Windows.WindowStartupLocation.Manual
        };
        window.Show();
        window.ShowPreviewPage(page);
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var checks = window.CheckPreviewLayout();
        File.WriteAllText(output + ".layout.json", System.Text.Json.JsonSerializer.Serialize(checks, Brain.Json));
        System.Windows.Window target = window;
        if (page is "Welcome" or "Performance" or "VoiceVolume" or "ProfileName") {
            target = page == "Welcome" ? window.CreateWelcomeWindow() : page == "Performance" ? window.CreatePerformanceWindow() : page == "VoiceVolume" ? window.CreateVoiceVolumeWindow() : window.CreateProfileNameWindow("New mod profile", "My fellowship", out _); target.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
            target.Left = -20000; target.Top = -20000; target.ShowInTaskbar = false; target.Show(); target.UpdateLayout();
        }
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(target.ActualWidth));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(target.ActualHeight));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(target);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(output)) encoder.Save(stream);
        if (page == "Dropdown") {
            var combo=(System.Windows.Controls.ComboBox)window.FindName("ModsProfileCombo");
            var popup=(System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup",combo);
            if(popup.Child is System.Windows.FrameworkElement child) {
                child.UpdateLayout();
                var list=new RenderTargetBitmap((int)Math.Ceiling(child.ActualWidth),(int)Math.Ceiling(child.ActualHeight),96,96,PixelFormats.Pbgra32);list.Render(child);
                var file=new PngBitmapEncoder();file.Frames.Add(BitmapFrame.Create(list));using(var stream=File.Create(output+".popup.png"))file.Save(stream);
            }
        }
        if (target != window) target.Close();
        window.Close();
        if (!runtime.IsDisposed) runtime.Close();
        app.Shutdown();
    }
}
