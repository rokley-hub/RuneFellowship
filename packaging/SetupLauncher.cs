using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Rune Fellowship Setup")]
[assembly: AssemblyProduct("Rune Fellowship")]
[assembly: AssemblyVersion("0.4.26.0")]
[assembly: AssemblyFileVersion("0.4.26.0")]
internal static class SetupLauncher
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool check = args.Length == 1 && args[0] == "--check";
        try
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string relative in new[] { "Setup Rune.ps1", "package_tools.py", "files.json", "runtime\\python\\python.exe" })
                if (!File.Exists(Path.Combine(root, relative)))
                    throw new InvalidOperationException("Extract the entire Rune download first, then open Install Rune.exe from the extracted folder. Missing: " + relative);
            if (check) return 0;
            string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
            var start = new ProcessStartInfo(shell, "-NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "Setup Rune.ps1") + "\"");
            start.WorkingDirectory = root;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            using (var process = Process.Start(start))
            {
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("Setup could not finish. Keep the complete extracted folder and try Setup Rune.cmd for details.");
            }
            return 0;
        }
        catch (Exception error)
        {
            if (!check) MessageBox.Show(error.Message, "Rune Fellowship setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
