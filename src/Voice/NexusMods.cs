using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Rune.Voice;

internal static class NexusMods
{
    internal const string BrowseUrl = "https://www.nexusmods.com/games/valheim/mods";
    internal static string IdFromUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !(uri.Host == "www.nexusmods.com" || uri.Host == "nexusmods.com") || uri.UserInfo.Length != 0)
            throw new IOException("Paste a Valheim mod page from nexusmods.com.");
        var match = Regex.Match(uri.AbsolutePath, @"^/(?:games/)?valheim/mods/([1-9][0-9]{0,9})/?$", RegexOptions.IgnoreCase);
        if (!match.Success) throw new IOException("Use the mod's page address, ending in /valheim/mods/1234, not a download link.");
        return "Nexus-" + match.Groups[1].Value;
    }
    internal static bool IsNexus(string id) => Regex.IsMatch(id, @"^Nexus-[1-9][0-9]{0,9}$");
    internal static string Page(string id) => IsNexus(id) ? "https://www.nexusmods.com/valheim/mods/" + id[6..] : throw new IOException("Invalid Nexus mod identifier.");
}

internal static partial class OwnedMods
{
    internal static void InstallNexus(string profile, string archivePath, string pageUrl, string name, string version)
    {
        string id = NexusMods.IdFromUrl(pageUrl);
        name = name.Trim(); version = version.Trim();
        if (name.Length is < 1 or > 100 || version.Length is < 1 or > 40 || name.Any(char.IsControl) || version.Any(char.IsControl))
            throw new IOException("Enter a mod name and the version shown on its download page.");
        if (!Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Select a ZIP download. FOMOD installers, RAR and 7z archives are not supported yet.");
        using var input = File.OpenRead(archivePath);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > 100000 || zip.Entries.Sum(e => e.Length) > 8_000_000_000L) throw new IOException("This mod exceeds the 8 GB / 100,000-file import limit.");
        var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\')).ToArray();
        foreach (var entry in zip.Entries) {
            string path = entry.FullName.Replace('\\','/').TrimEnd('/');
            if (path.Length == 0 || path.Split('/').Any(p => p.Length == 0 || p == "." || Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))) throw new IOException("Invalid archive path.");
            _ = SafePath(profile, path);
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Linked archive entries are unsupported.");
            if (path.Split('/').Any(p => p.Equals("fomod", StringComparison.OrdinalIgnoreCase))) throw new IOException("This mod needs a FOMOD installer. Choose a plain BepInEx ZIP from the author instead.");
        }
        string[] names = entries.Select(e => e.FullName.Replace('\\','/')).ToArray();
        string prefix = names.Length > 0 && names.All(p => p.Contains('/')) ? names[0].Split('/')[0] : "";
        bool strip = prefix.Length > 0 && names.All(p => p.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            && !new[] { "BepInEx", "plugins", "patchers", "monomod", "config" }.Contains(prefix, StringComparer.OrdinalIgnoreCase);
        var mapped = new List<(ZipArchiveEntry Entry, string Path)>();
        foreach (var entry in entries) {
            string path = entry.FullName.Replace('\\','/'); if (strip) path = path[(prefix.Length+1)..];
            if (path.StartsWith("BepInEx/",StringComparison.OrdinalIgnoreCase)) path=path[8..];
            string ext=Path.GetExtension(path).ToLowerInvariant();
            if (new[] { ".exe", ".msi", ".cmd", ".bat", ".ps1", ".sh", ".zip", ".7z", ".rar", ".lnk", ".old" }.Contains(ext))
                throw new IOException("This ZIP contains an installer, script, nested archive or disabled file. Use a plain BepInEx mod ZIP.");
            if (path.StartsWith("core/",StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals("winhttp.dll",StringComparison.OrdinalIgnoreCase))
                throw new IOException("Install BepInEx using Set up required mods, not this mod importer.");
            string? category=new[] { "plugins", "patchers", "monomod", "config" }.FirstOrDefault(c=>path.StartsWith(c+"/",StringComparison.OrdinalIgnoreCase));
            if (category == null && !path.Contains('/') && (path.Equals("manifest.json",StringComparison.OrdinalIgnoreCase) || path.Equals("icon.png",StringComparison.OrdinalIgnoreCase))) continue;
            string relative= category == null ? "BepInEx/plugins/"+id+"/"+path : "BepInEx/"+category+"/"+(category=="config"?"":id+"/")+path[(category.Length+1)..];
            if (mapped.Any(m=>m.Path.Equals(relative,StringComparison.OrdinalIgnoreCase))) throw new IOException("Duplicate archive destination.");
            mapped.Add((entry,relative));
        }
        if (!mapped.Any(m => !IsConfig(m.Path) && m.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("No BepInEx plugin DLL found. Texture-only packages, blueprints and other special layouts need their own installer.");
        if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(profile))!).AvailableFreeSpace < mapped.Sum(m=>m.Entry.Length)+500_000_000L) throw new IOException("Not enough disk space to import this mod.");
        Transaction(profile, stage => {
            var mods=Read(stage); var current=mods.FirstOrDefault(m=>m.Id==id);
            var dllNames=mapped.Where(m=>m.Path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)).Select(m=>Path.GetFileName(m.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (mods.Where(m=>m.Id!=id).Any(m=>m.Files.Any(f=>dllNames.Contains(Path.GetFileName(f)))))
                throw new IOException("A DLL with the same name is already installed, possibly from Thunderstore. Remove that copy first to avoid loading the mod twice.");
            if (current!=null) { RemoveFiles(stage,current);mods.Remove(current); }
            foreach (var item in mapped) {
                string target=SafePath(stage,item.Path);
                if (mods.Any(m=>m.Files.Contains(item.Path,StringComparer.OrdinalIgnoreCase))) throw new IOException("Another mod owns "+item.Path);
                if (IsConfig(item.Path) && File.Exists(target)) continue;
                if (File.Exists(target)||File.Exists(target+".old")) throw new IOException("An untracked file already exists: "+item.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var src=item.Entry.Open();using var dst=File.Create(target);src.CopyTo(dst);
            }
            var installed=new OwnedMod(id,name,version,true,Array.Empty<string>(),mapped.Select(m=>m.Path).ToArray());
            if (current?.Enabled==false) { SetEnabled(stage,installed,false);installed=installed with {Enabled=false}; }
            mods.Add(installed);Save(stage,mods);
        });
    }
}
