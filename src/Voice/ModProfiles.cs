using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Rune.Voice;

internal sealed record InstalledMod(string Id, string Name, string Version, bool Enabled, string[] Dependencies, string[] Folders, string Block);
internal static class ModProfiles
{
    internal static string Scalar(string block, string key) => Regex.Match(block, @"(?m)^  " + Regex.Escape(key) + @":\s*([^\r\n]+)").Groups[1].Value.Trim().Trim('\'', '"');
    internal static List<InstalledMod> Read(string profile)
    {
        if (File.Exists(Path.Combine(profile, "rune-profile.json"))) return OwnedMods.Read(profile).Select(m => new InstalledMod(m.Id, m.Name, m.Version, m.Enabled, m.Dependencies, m.Files.Select(f => Path.GetDirectoryName(Path.Combine(profile, f))!).Distinct().ToArray(), "owned")).ToList();
        var result = new List<InstalledMod>();
        string yaml = Path.Combine(profile, "mods.yml");
        if (File.Exists(yaml)) {
            foreach (Match match in Regex.Matches(File.ReadAllText(yaml), @"(?ms)^- manifestVersion:.*?(?=^- manifestVersion:|\z)")) {
                string block = match.Value, id = Scalar(block, "name");
                if (!Regex.IsMatch(id, @"^[A-Za-z0-9_-]+$")) continue;
                string version = string.Join(".", new[] { "major", "minor", "patch" }.Select(k => Regex.Match(block, @"(?m)^    " + k + @":\s*(\d+)").Groups[1].Value));
                var deps = Regex.Match(block, @"(?ms)^  dependencies:\s*\r?\n(?<deps>(?:    - [^\r\n]+\r?\n)*)").Groups["deps"].Value;
                string[] folders = new[] { "plugins", "patchers", "monomod", "config" }.Select(d => Path.Combine(profile, "BepInEx", d, id)).Where(Directory.Exists).ToArray();
                result.Add(new InstalledMod(id, Scalar(block, "displayName"), version, Scalar(block, "enabled") == "true", Regex.Matches(deps, @"(?m)^    - (.+)$").Cast<Match>().Select(m => m.Groups[1].Value.Trim()).ToArray(), folders, block));
            }
        }
        string plugins = Path.Combine(profile, "BepInEx", "plugins");
        if (Directory.Exists(plugins)) foreach (string dir in Directory.GetDirectories(plugins)) {
            if (result.Any(m => m.Folders.Contains(dir))) continue;
            string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            if (!files.Any(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dll.old", StringComparison.OrdinalIgnoreCase))) continue;
            string id = Path.GetFileName(dir);
            string[] dependencies = id == "RuneCompanion" ? new[] { "ValheimModding-Jotunn-2.28.0" } : id == "PlanBuild" ? new[] { "ValheimModding-Jotunn-2.28.0", "ValheimModding-HookGenPatcher-0.0.4" } : Array.Empty<string>();
            result.Add(new InstalledMod(id, id, "Local", files.Any(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)), dependencies, new[] { dir }, ""));
        }
        // Direct installations do not have a manifest. Include the patcher for dependency checks.
        string patchers = Path.Combine(profile, "BepInEx", "patchers");
        if (Directory.Exists(patchers)) foreach (string dir in Directory.GetDirectories(patchers)) if (!result.Any(m => m.Folders.Contains(dir))) result.Add(new InstalledMod(Path.GetFileName(dir), Path.GetFileName(dir), "Local", Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories).Length > 0, Array.Empty<string>(), new[] { dir }, ""));
        return result;
    }
    private static string DepName(string dep) => Regex.Replace(dep, @"-\d+\.\d+\.\d+$", "");
    private static bool Matches(InstalledMod mod, string dep) => mod.Id == DepName(dep) || (mod.Block.Length == 0 && (DepName(dep).EndsWith("-" + mod.Id, StringComparison.OrdinalIgnoreCase) || mod.Id.EndsWith("." + DepName(dep).Split('-').Last(), StringComparison.OrdinalIgnoreCase)));
    internal static string DependencyError(List<InstalledMod> mods, InstalledMod selected, bool enable)
    {
        if (enable) {
            var missing = selected.Dependencies.Where(d => !mods.Any(m => m.Enabled && Matches(m, d) && (!Version.TryParse(Regex.Match(d, @"-(\d+\.\d+\.\d+)$").Groups[1].Value, out var required) || !Version.TryParse(m.Version, out var installed) || installed >= required))).ToArray();
            if (missing.Length > 0) return "Enable/install these dependencies first: " + string.Join(", ", missing);
        } else {
            var dependants = mods.Where(m => m.Enabled && m.Dependencies.Any(d => Matches(selected, d))).Select(m => m.Name).ToArray();
            if (dependants.Length > 0) return "Disable these dependent mods first: " + string.Join(", ", dependants);
        }
        return "";
    }
    internal static bool GameRunning => Process.GetProcessesByName("valheim").Any();
    internal static void Toggle(string profile, string id) => OwnedMods.Toggle(profile, id);
    internal static IEnumerable<string> SafeFiles(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked profile folders are not supported.");
        foreach (string file in Directory.GetFiles(directory)) { if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked mod files are not supported."); yield return file; }
        foreach (string child in Directory.GetDirectories(directory)) foreach (string file in SafeFiles(child)) yield return file;
    }
    private static void CheckPath(string root, string path)
    {
        if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("The mod path is outside its profile.");
        string? current = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        while (current != null && current.Length >= root.TrimEnd(Path.DirectorySeparatorChar).Length) {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked profile paths are not supported.");
            current = Path.GetDirectoryName(current);
        }
    }
    internal static ProcessStartInfo LaunchInfo(string executable, string profile, bool modded)
    {
        if (!File.Exists(executable) || Path.GetFileName(executable).ToLowerInvariant() != "valheim.exe") throw new FileNotFoundException("Choose your Valheim executable in the Mods tab.");
        string preloader = Path.Combine(profile, "BepInEx", "core", "BepInEx.Preloader.dll");
        if (modded && !File.Exists(preloader)) throw new FileNotFoundException("Install BepInExPack_Valheim from Browse mods first.");
        string version = File.Exists(Path.Combine(profile, ".doorstop_version")) ? File.ReadAllText(Path.Combine(profile, ".doorstop_version")).Trim() : "4";
        var info = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false };
        info.ArgumentList.Add(version.StartsWith("3") ? "--doorstop-enable" : "--doorstop-enabled"); info.ArgumentList.Add(modded ? "true" : "false");
        if (modded) { info.ArgumentList.Add(version.StartsWith("3") ? "--doorstop-target" : "--doorstop-target-assembly"); info.ArgumentList.Add(preloader); }
        info.Environment["SteamAppId"] = "892970";
        return info;
    }
}
