using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal static class Launcher
{
    [STAThread] private static void Main()
    {
        try {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            var info = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "Start Rune.ps1") + "\"");
            info.UseShellExecute = false; info.CreateNoWindow = true; info.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(info);
        } catch (Exception e) { MessageBox.Show(e.Message, "Rune could not start"); }
    }
}
