using System.Diagnostics;
using System.Text.Json;

namespace Rune.Voice;

internal static class UiPerformance
{
    public static void Run(string output)
    {
        ApplicationConfiguration.Initialize();
        var watch = Stopwatch.StartNew();
        using var form = new RuneWindow(Path.Combine(Path.GetDirectoryName(output)!, "preview-bridge"));
        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
        double startupMs = watch.Elapsed.TotalMilliseconds;
        var repaints = new List<double>();
        using (var bitmap = new Bitmap(form.Width, form.Height))
            for (int i = 0; i < 4; i++)
            {
                watch.Restart(); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                repaints.Add(watch.Elapsed.TotalMilliseconds);
            }
        watch.Restart();
        foreach (var size in new[] { new Size(1180, 800), new Size(1440, 900), new Size(1360, 850) })
        {
            form.ClientSize = size; form.PerformLayout(); Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        }
        double resizeMs = watch.Elapsed.TotalMilliseconds;
        int buttonImagesBeforeStress = RuneTheme.LiveButtonImages;
        using (var button = new Button())
            for (int i = 0; i < 160; i++)
            {
                button.Size = new Size(180 + i, 54);
                RuneTheme.SetButtonTone(button, RuneButtonTone.Navigation);
            }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int buttonImagesAfterStress = RuneTheme.LiveButtonImages;
        bool artworkFilesUnlocked = true;
        foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Assets"), "*.png"))
        {
            try { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); }
            catch (IOException) { artworkFilesUnlocked = false; }
        }
        using var process = Process.GetCurrentProcess(); process.Refresh();
        File.WriteAllText(output, JsonSerializer.Serialize(new { startupMs, repaintMs = repaints, resizeMs,
            privateMemoryMb = process.PrivateMemorySize64 / 1048576d, buttonImagesBeforeStress, buttonImagesAfterStress, artworkFilesUnlocked,
            note = "Hidden WinForms render benchmark; synthetic profiles; no microphone or game commands." }, new JsonSerializerOptions { WriteIndented = true }));
        form.Close();
    }
}
