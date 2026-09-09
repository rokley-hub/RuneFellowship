using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
namespace Rune.Voice;

internal sealed record OwnedMod(string Id, string Name, string Version, bool Enabled, string[] Dependencies, string[] Files);
internal static partial class OwnedMods
{
    internal const string Manifest = "rune-profile.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static List<OwnedMod> Read(string profile) => JsonSerializer.Deserialize<List<OwnedMod>>(File.ReadAllText(Path.Combine(profile, Manifest))) ?? new();
    internal static void Save(string profile, List<OwnedMod> mods) => AtomicText(Path.Combine(profile, Manifest), JsonSerializer.Serialize(mods, Json));
    private static void AtomicText(string path, string text) { string temp = path + ".tmp-" + Guid.NewGuid().ToString("N"); File.WriteAllText(temp, text); File.Move(temp, path, true); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string newName, string existingName, IntPtr security);
    private static void CloneForTransaction(string source, string target)
    {
        Directory.CreateDirectory(target);
        // Only immutable libraries/visual assets share disk blocks within Rune's own history.
        // Mutable settings/data are copied. Package updates always replace files, never edit them in place.
        var immutable = new HashSet<string>(new[] { ".dll", ".bundle", ".assetbundle", ".png", ".jpg", ".jpeg", ".dds", ".tga", ".unity3d", ".assets", ".resource", ".ress" }, StringComparer.OrdinalIgnoreCase);
        foreach (string file in ModProfiles.SafeFiles(source)) {
            string relative = Path.GetRelativePath(source, file), destination = SafePath(target, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string extension = Path.GetExtension(file.EndsWith(".old", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file);
            bool link = !IsConfig(relative) && (immutable.Contains(extension) || IsUnityBundle(file));
            if (!link || !CreateHardLink(destination, file, IntPtr.Zero)) File.Copy(file, destination);
        }
    }
    private static bool IsUnityBundle(string file)
    {
        using var stream = File.OpenRead(file); Span<byte> header = stackalloc byte[8]; int count = stream.Read(header);
        return count == 8 && (header[..7].SequenceEqual("UnityFS"u8) || header[..8].SequenceEqual("UnityRaw"u8) || header[..8].SequenceEqual("UnityWeb"u8) || header[..4].SequenceEqual("BVTB"u8));
    }
    internal static string[] Profiles(string root) => Directory.Exists(root) ? Directory.GetDirectories(root).Where(d => !Path.GetFileName(d).StartsWith('.') && File.Exists(Path.Combine(d, Manifest))).ToArray() : Array.Empty<string>();
    internal static string SafePath(string root, string relative)
    {
        string normalized = relative.Replace('\\', '/');
        if (Path.IsPathRooted(relative) || normalized.Split('/').Any(s => s == ".." || s.Contains(':') || s.EndsWith(' ') || s.EndsWith('.'))) throw new IOException("Unsafe package path: " + relative);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Package path escaped its profile.");
        return full;
    }
    internal static string NewPath(string root, string name)
    {
        if (!Regex.IsMatch(name, @"^[\p{L}\p{N}][\p{L}\p{N} _-]{0,47}$")) throw new IOException("Use a profile name of 1–48 letters, numbers, spaces, hyphens or underscores.");
        string path = SafePath(root, name.Trim()); if (Directory.Exists(path)) throw new IOException("A profile with that name already exists."); return path;
    }
    internal static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in ModProfiles.SafeFiles(source)) {
            string relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith("cache" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
            string destination = SafePath(target, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination, true);
        }
    }
    internal static string Create(string root, string name)
    {
        string target = NewPath(root, name); Directory.CreateDirectory(target); Save(target, new()); BlueprintProfiles.Prepare(target, target); return target;
    }
    internal static string RemoveProfile(string root, string profile)
    {
        if (ModProfiles.GameRunning) throw new IOException("Close Valheim before removing a mod profile.");
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullProfile = Path.GetFullPath(profile).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(fullProfile)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), fullRoot, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(fullProfile, Manifest))) throw new IOException("Only a Rune-owned profile can be removed.");
        _ = ModProfiles.SafeFiles(fullProfile).ToArray();
        string removedRoot = Path.Combine(fullRoot, ".backups", "removed-profiles");
        Directory.CreateDirectory(removedRoot);
        string backup = Path.Combine(removedRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Path.GetFileName(fullProfile));
        Directory.Move(fullProfile, backup);
        return backup;
    }
    internal static string Import(string root, string name, string source)
    {
        string target = NewPath(root, name); Directory.CreateDirectory(root); string staging = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N"));
        try {
            if (!Directory.Exists(Path.Combine(source, "BepInEx"))) throw new IOException("Choose a profile containing BepInEx.");
            var originals = ModProfiles.Read(source); Directory.CreateDirectory(staging);
            foreach (string dir in new[] { "BepInEx", "doorstop_libs" }) if (Directory.Exists(Path.Combine(source, dir))) CopyTree(Path.Combine(source, dir), Path.Combine(staging, dir));
            foreach (string file in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" }) if (File.Exists(Path.Combine(source, file))) File.Copy(Path.Combine(source, file), Path.Combine(staging, file));
            var result = new List<OwnedMod>();
            if (File.Exists(Path.Combine(source, Manifest))) result = Read(source);
            else foreach (var mod in originals) {
                var files = mod.Folders.SelectMany(ModProfiles.SafeFiles).Select(f => Path.GetRelativePath(source, f)).ToList();
                if (mod.Id.Contains("BepInExPack")) {
                    foreach (string dir in new[] { "BepInEx/core", "doorstop_libs" }) if (Directory.Exists(Path.Combine(source, dir))) files.AddRange(ModProfiles.SafeFiles(Path.Combine(source, dir)).Select(f => Path.GetRelativePath(source, f)));
                    files.AddRange(new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" }.Where(f => File.Exists(Path.Combine(source, f))));
                }
                result.Add(new(mod.Id, mod.Name, mod.Version, mod.Enabled, mod.Dependencies, files.Select(f => f.EndsWith(".old", StringComparison.OrdinalIgnoreCase) ? f[..^4] : f).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
            }
            if (!File.Exists(Path.Combine(source, Manifest))) {
                string plugins = Path.Combine(staging, "BepInEx", "plugins");
                if (Directory.Exists(plugins)) {
                    var tracked = new HashSet<string>(result.SelectMany(m => m.Files).Select(f => f.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
                    var loose = ModProfiles.SafeFiles(plugins).Select(f => Path.GetRelativePath(staging, f)).Where(f => !tracked.Contains((f.EndsWith(".old", StringComparison.OrdinalIgnoreCase) ? f[..^4] : f).Replace('\\', '/'))).ToArray();
                    foreach (bool enabled in new[] { true, false }) {
                        var files = loose.Where(f => f.EndsWith(".old", StringComparison.OrdinalIgnoreCase) != enabled).Select(f => enabled ? f : f[..^4]).ToArray();
                        if (files.Length > 0) result.Add(new("LocalExtras" + (enabled ? "" : "Disabled"), "Local plugins & texture files" + (enabled ? "" : " (disabled)"), "Local", enabled, Array.Empty<string>(), files));
                    }
                }
            }
            Save(staging, result); BlueprintProfiles.Prepare(staging, target, source); File.WriteAllText(Path.Combine(staging, "import-origin.txt"), "Copied from " + source + " at " + DateTime.Now.ToString("O") + ". This profile is independent; the source is not used at runtime. Blueprint files inside the selected source were copied; external blueprint libraries must be imported separately."); Directory.Move(staging, target); return target;
        } finally { if (Directory.Exists(staging)) DeleteInside(root, staging); }
    }
    private static void DeleteInside(string root, string directory)
    {
        SafePath(root, Path.GetRelativePath(root, directory)); _ = ModProfiles.SafeFiles(directory).ToArray(); Directory.Delete(directory, true);
    }
    internal static void Transaction(string profile, Action<string> action)
    {
        if (!File.Exists(Path.Combine(profile, Manifest))) throw new IOException("Only Rune-owned profiles can be changed.");
        if (ModProfiles.GameRunning) throw new IOException("Close Valheim before changing mods.");
        string root = Path.GetDirectoryName(profile)!, stage = Path.Combine(root, ".stage-" + Guid.NewGuid().ToString("N"));
        string history = Path.Combine(root, ".backups", Path.GetFileName(profile)); Directory.CreateDirectory(history);
        string backup = Path.Combine(history, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6]);
        try {
            CloneForTransaction(profile, stage); action(stage);
            if (ModProfiles.GameRunning) throw new IOException("Valheim started during the change. Close it and retry.");
            Directory.Move(profile, backup);
            try { Directory.Move(stage, profile); } catch { Directory.Move(backup, profile); throw; }
            foreach (string old in Directory.GetDirectories(history).OrderByDescending(d => d).Skip(3)) DeleteInside(history, old);
        } finally { if (Directory.Exists(stage)) DeleteInside(root, stage); }
    }
    internal static void Toggle(string profile, string id)
    {
        var live = ModProfiles.Read(profile); var selected = live.Single(m => m.Id == id);
        if (id.Contains("BepInExPack")) throw new IOException("BepInEx is required by the profile. Use Start vanilla to play without mods.");
        string error = ModProfiles.DependencyError(live, selected, !selected.Enabled); if (error.Length > 0) throw new IOException(error);
        Transaction(profile, stage => { var mods = Read(stage); var mod = mods.Single(m => m.Id == id); SetEnabled(stage, mod, !mod.Enabled); mods[mods.IndexOf(mod)] = mod with { Enabled = !mod.Enabled }; Save(stage, mods); });
    }
    private static void SetEnabled(string stage, OwnedMod mod, bool enabled)
    {
        foreach (string relative in mod.Files.Where(f => !IsConfig(f))) {
            string normal = SafePath(stage, relative), old = normal + ".old"; string source = enabled ? old : normal, target = enabled ? normal : old;
            if (!File.Exists(source)) continue;
            if (File.Exists(target)) throw new IOException("Conflicting enabled and disabled files: " + relative);
            File.Move(source, target);
        }
    }
    internal static bool IsConfig(string path) => path.Replace('\\', '/').StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase);
    internal static string[] ConfigFilesFor(string profile, string modId)
    {
        if (!File.Exists(Path.Combine(profile, Manifest))) throw new IOException("Only Rune-owned profiles can be configured.");
        string directory = Path.Combine(profile, "BepInEx", "config");
        if (!Directory.Exists(directory)) return Array.Empty<string>();
        string[] allowed = { ".cfg", ".json", ".yml", ".yaml", ".ini" };
        var all = ModProfiles.SafeFiles(directory).Where(f => allowed.Contains(Path.GetExtension(f).ToLowerInvariant())).Select(f => Path.GetRelativePath(profile, f)).ToArray();
        var owned = Read(profile).FirstOrDefault(m => m.Id.Equals(modId, StringComparison.OrdinalIgnoreCase));
        var explicitFiles = owned?.Files.Where(IsConfig).Where(f => File.Exists(SafePath(profile, f))).ToArray() ?? Array.Empty<string>();
        if (explicitFiles.Length > 0) return explicitFiles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToArray();
        var visible = ModProfiles.Read(profile).FirstOrDefault(m => m.Id.Equals(modId, StringComparison.OrdinalIgnoreCase));
        string packageName = modId.Contains('-') ? modId[(modId.IndexOf('-') + 1)..] : modId;
        var tokens = Regex.Matches(packageName + " " + (visible?.Name ?? owned?.Name ?? ""), @"[A-Za-z0-9]{4,}").Cast<Match>().Select(m => m.Value.ToLowerInvariant()).Where(t => t is not "valheim" and not "official" and not "plugin" and not "modpack").Distinct().ToArray();
        return all.Where(f => { string normalized = Regex.Replace(Path.GetFileNameWithoutExtension(f), "[^A-Za-z0-9]", "").ToLowerInvariant(); return tokens.Any(normalized.Contains); }).OrderBy(f => f).ToArray();
    }
    internal static void Remove(string profile, string id)
    {
        var live = ModProfiles.Read(profile); var selected = live.Single(m => m.Id == id);
        if (id.Contains("BepInExPack")) throw new IOException("BepInEx cannot be removed from a modded profile.");
        // Disabled dependants also need the package on disk for later re-enabling.
        string error = ModProfiles.DependencyError(live.Select(m => m with { Enabled = true }).ToList(), selected, false); if (error.Length > 0) throw new IOException(error);
        Transaction(profile, stage => { var mods = Read(stage); var mod = mods.Single(m => m.Id == id); RemoveFiles(stage, mod); mods.Remove(mod); Save(stage, mods); });
    }
    private static void RemoveFiles(string stage, OwnedMod mod)
    {
        foreach (string relative in mod.Files.Where(f => !IsConfig(f))) { string file = SafePath(stage, relative); if (File.Exists(file)) File.Delete(file); if (File.Exists(file + ".old")) File.Delete(file + ".old"); }
    }
    internal static async Task Install(string profile, List<CatalogMod> requested, List<CatalogMod> catalog, IProgress<string> progress, CancellationToken token, bool enableRequested = false)
    {
        var plan = ModCatalog.Resolve(requested, catalog, Read(profile)); var downloads = new Dictionary<string, string>();
        string downloadFolder = Path.Combine(Path.GetDirectoryName(profile)!, ".download-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(downloadFolder);
        try {
        foreach (var mod in plan) {
            token.ThrowIfCancellationRequested();
            var current = Read(profile).FirstOrDefault(m => m.Id == mod.Id);
            if (current != null && current.Version == mod.Version) continue;
            progress.Report("Downloading " + mod.Name + " " + mod.Version + "…");
            string archive = SafePath(downloadFolder, mod.Id + ".zip"); await ModCatalog.DownloadToFile(mod.Download, archive, token); downloads[mod.Id] = archive;
        }
        token.ThrowIfCancellationRequested(); progress.Report("Installing mods and dependencies…");
        Transaction(profile, stage => {
            var mods = Read(stage);
            foreach (var mod in plan) {
                token.ThrowIfCancellationRequested(); var current = mods.FirstOrDefault(m => m.Id == mod.Id);
                if (downloads.TryGetValue(mod.Id, out var archive)) {
                    bool enabled = current?.Enabled ?? true;
                    if (current != null) { RemoveFiles(stage, current); mods.Remove(current); }
                    using var input = File.OpenRead(archive); var installed = InstallArchive(stage, mod, input, mods);
                    if (!enabled) { SetEnabled(stage, installed, false); installed = installed with { Enabled = false }; }
                    mods.Add(installed);
                }
            }
            // Dependencies of newly enabled requests must be enabled recursively; preserve disabled roots on updates.
            void Enable(string id, HashSet<string> seen) {
                if (!seen.Add(id)) return; var mod = mods.Single(m => m.Id == id);
                foreach (string dep in mod.Dependencies) Enable(dep[..dep.LastIndexOf('-')], seen);
                if (!mod.Enabled) { SetEnabled(stage, mod, true); mods[mods.IndexOf(mod)] = mod with { Enabled = true }; }
            }
            foreach (var root in requested) if (enableRequested || mods.Single(m => m.Id == root.Id).Enabled) Enable(root.Id, new());
            token.ThrowIfCancellationRequested();
            Save(stage, mods);
        });
        } finally { if (Directory.Exists(downloadFolder)) DeleteInside(Path.GetDirectoryName(profile)!, downloadFolder); }
    }
    internal static OwnedMod InstallArchive(string stage, CatalogMod mod, byte[] bytes, List<OwnedMod> others)
    {
        using var input = new MemoryStream(bytes); return InstallArchive(stage, mod, input, others);
    }
    private static OwnedMod InstallArchive(string stage, CatalogMod mod, Stream input, List<OwnedMod> others)
    {
        if (!Regex.IsMatch(mod.Id, @"^[A-Za-z0-9_]+-[A-Za-z0-9_]+$")) throw new IOException("Invalid package identifier.");
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, true);
        long unpacked = zip.Entries.Sum(e => e.Length);
        if (zip.Entries.Count > 100000 || unpacked > 40_000_000_000L) throw new IOException("Package is too large to extract.");
        if (new DriveInfo(Path.GetPathRoot(stage)!).AvailableFreeSpace < unpacked + 500_000_000) throw new IOException("There is not enough disk space to unpack this mod safely.");
        var manifest = zip.GetEntry("manifest.json") ?? throw new IOException("This download has no Thunderstore manifest.");
        using (var doc = JsonDocument.Parse(manifest.Open())) {
            if (doc.RootElement.GetProperty("version_number").GetString() != mod.Version || doc.RootElement.GetProperty("name").GetString() != mod.Id[(mod.Id.IndexOf('-') + 1)..]) throw new IOException("Downloaded package does not match the selected version.");
        }
        bool core = mod.Id == "denikson-BepInExPack_Valheim"; var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries) {
            string path = entry.FullName.Replace('\\', '/'); SafePath(stage, path.TrimEnd('/'));
            if (path.EndsWith('/')) continue;
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Package contains a symbolic link.");
            string? relative = null;
            if (core) {
                if (path.StartsWith("BepInExPack_Valheim/", StringComparison.OrdinalIgnoreCase)) relative = path["BepInExPack_Valheim/".Length..];
            } else {
                if (path.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)) path = path[8..];
                string? category = new[] { "plugins", "patchers", "monomod", "config" }.FirstOrDefault(c => path.StartsWith(c + "/", StringComparison.OrdinalIgnoreCase));
                if (category != null) relative = "BepInEx/" + category + "/" + (category == "config" ? "" : mod.Id + "/") + path[(category.Length + 1)..];
                else if (path.Contains('/') || !(path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || path.Equals("icon.png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))) relative = "BepInEx/plugins/" + mod.Id + "/" + path;
                if (relative != null && (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))) throw new IOException("This is a desktop installer, not a supported Valheim mod package.");
            }
            if (relative == null && (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).StartsWith("NOTICE", StringComparison.OrdinalIgnoreCase)))
                relative = "BepInEx/docs/" + mod.Id + "/" + path;
            if (relative == null) continue;
            string target = SafePath(stage, relative);
            if (!files.Add(relative)) throw new IOException("Package has duplicate file paths.");
            if (IsConfig(relative) && File.Exists(target)) continue;
            if (others.Any(m => m.Files.Contains(relative, StringComparer.OrdinalIgnoreCase))) throw new IOException("Another mod owns " + relative);
            if (File.Exists(target) || File.Exists(target + ".old")) throw new IOException("An untracked file already exists: " + relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); using var source = entry.Open(); using var output = File.Create(target); source.CopyTo(output);
        }
        if (files.Count == 0 && mod.Dependencies.Length == 0) throw new IOException("No supported mod files were found.");
        return new(mod.Id, mod.Name, mod.Version, true, mod.Dependencies, files.ToArray());
    }
    internal static void SaveConfig(string profile, string relative, string text)
    {
        if (!IsConfig(relative) || !new[] { ".cfg", ".json", ".yml", ".yaml", ".ini" }.Contains(Path.GetExtension(relative).ToLowerInvariant())) throw new IOException("Select a supported configuration file.");
        Transaction(profile, stage => AtomicText(SafePath(stage, relative), text));
    }
    internal static void AddRune(string profile, string bridge, string dll)
    {
        if (!File.Exists(dll)) throw new IOException("Rune's game plugin is missing from this app installation.");
        Transaction(profile, stage => {
            var mods = Read(stage); var existing = mods.FirstOrDefault(m => m.Id == "RuneCompanion");
            if (existing != null) { RemoveFiles(stage, existing); mods.Remove(existing); }
            string relative = "BepInEx/plugins/RuneCompanion/RuneCompanion.dll";
            string destination = SafePath(stage, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(dll, destination, true);
            var rune = new OwnedMod("RuneCompanion", "Rune Fellowship", Rune.Shared.Release.Gameplay, existing?.Enabled ?? true, new[] { "ValheimModding-Jotunn-2.28.0" }, new[] { relative });
            if (!rune.Enabled) SetEnabled(stage, rune, false);
            mods.Add(rune);
            string config = SafePath(stage, "BepInEx/config/local.rune.companion.cfg"); Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            string text = File.Exists(config) ? File.ReadAllText(config) : "[General]\n";
            if (Regex.IsMatch(text, @"(?m)^BridgeFolder\s*=")) text = Regex.Replace(text, @"(?m)^BridgeFolder\s*=.*$", _ => "BridgeFolder = " + bridge);
            else text += "\nBridgeFolder = " + bridge + "\n";
            AtomicText(config, text); Save(stage, mods);
        });
    }
    internal static void RestoreLast(string profile)
    {
        if (ModProfiles.GameRunning) throw new IOException("Close Valheim before restoring a profile.");
        string history = Path.Combine(Path.GetDirectoryName(profile)!, ".backups", Path.GetFileName(profile));
        string? backup = Directory.Exists(history) ? Directory.GetDirectories(history).OrderByDescending(p => p).FirstOrDefault() : null;
        if (backup == null) throw new IOException("No backup is available yet.");
        // Copy before transaction, because normal history pruning must never remove our selected source.
        Transaction(profile, stage => {
            foreach (string file in ModProfiles.SafeFiles(stage).ToArray()) File.Delete(file);
            CloneForTransaction(backup, stage);
        });
    }
    internal static void PrepareLaunch(string executable, string profile)
    {
        string game = Path.GetDirectoryName(executable)!;
        foreach (string name in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" }) {
            string source = SafePath(profile, name), target = SafePath(game, name); if (!File.Exists(source)) { if (name == "winhttp.dll") throw new IOException("Install BepInExPack_Valheim before launching."); continue; }
            if (File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(source))) continue;
            if (File.Exists(target)) File.Copy(target, target + ".rune-backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            File.Copy(source, target, true);
        }
    }
}
