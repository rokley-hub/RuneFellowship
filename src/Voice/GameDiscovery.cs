using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace Rune.Voice;

internal static class GameDiscovery
{
    internal static string FindValheim()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try {
            foreach (var key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" })
                foreach (string value in new[] { "SteamPath", "InstallPath" })
                    if (Registry.GetValue(key, value, null) is string path && Directory.Exists(path)) roots.Add(path);
        } catch (System.Security.SecurityException) { }
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        foreach (string root in roots.ToArray()) {
            try {
                string libraries = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (File.Exists(libraries)) foreach (Match m in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s+\"([^\"]+)\""))
                    roots.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return roots.Select(root => Path.Combine(root, "steamapps", "common", "Valheim", "valheim.exe")).FirstOrDefault(File.Exists) ?? "";
    }
}
