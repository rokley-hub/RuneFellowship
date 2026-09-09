using System.Text.RegularExpressions;

namespace Rune.Voice;
internal static class BlueprintProfiles
{
    // Prepare only a new/imported Rune-owned profile. Never rewrite the source profile.
    internal static void Prepare(string staging, string destination, string? source = null)
    {
        string library = OwnedMods.SafePath(staging, "blueprints"); Directory.CreateDirectory(library);
        string config = OwnedMods.SafePath(staging, "BepInEx/config/marcopogo.PlanBuild.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        string text = File.Exists(config) ? File.ReadAllText(config) : "[Directories]\n";
        if (source != null) {
            string sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidates = new List<string> { Path.Combine(source, "blueprints") };
            foreach (Match match in Regex.Matches(text, @"(?m)^(?:Search|Save) directory\s*=\s*([^\r\n]+)")) {
                string value = match.Groups[1].Value.Trim();
                string path = Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(source, "BepInEx", value));
                // Import a library inside the selected source; external libraries remain untouched.
                if (path.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase)) candidates.Add(path);
            }
            foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
                foreach (string file in ModProfiles.SafeFiles(path).Where(f => Path.GetExtension(f).Equals(".blueprint", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(f).Equals(".vbuild", StringComparison.OrdinalIgnoreCase))) {
                    string target = OwnedMods.SafePath(library, Path.GetRelativePath(path, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (!File.Exists(target)) File.Copy(file, target);
                }
            if (File.Exists(config)) File.Copy(config, config + ".before-rune-import", true);
        }
        string final = Path.Combine(Path.GetFullPath(destination), "blueprints").Replace('\\', '/');
        foreach (string key in new[] { "Search directory", "Save directory" }) {
            string pattern = @"(?m)^" + Regex.Escape(key) + @"\s*=[^\r\n]*";
            if (Regex.IsMatch(text, pattern)) text = Regex.Replace(text, pattern, _ => key + " = " + final);
            else {
                if (!text.Contains("[Directories]")) text += "\n[Directories]\n";
                text = text.Replace("[Directories]", "[Directories]\n" + key + " = " + final);
            }
        }
        File.WriteAllText(config, text);
    }
}
