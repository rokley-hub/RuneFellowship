using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace Rune.Voice;

// A self-contained, versioned profile backup. Binary payloads are streamed as base64,
// so large texture packs never have to fit in memory alongside the desktop app.
internal static class ModProfileXml
{
    private const long MaxBytes = 16L * 1024 * 1024 * 1024;
    private const int MaxFiles = 100000;

    private static bool Included(string relative)
    {
        string path = relative.Replace('\\', '/');
        if (path.Split('/').Any(p => p.Equals("cache", StringComparison.OrdinalIgnoreCase) || p.Equals(".backups", StringComparison.OrdinalIgnoreCase))
            || path.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return false;
        return new[] { "BepInEx/", "doorstop_libs/", "blueprints/" }.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt", "start_game_bepinex.sh", "start_server_bepinex.sh" }.Contains(path.EndsWith(".old", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path, StringComparer.OrdinalIgnoreCase);
    }

    internal static void Export(string profile, string destination)
    {
        if (ModProfiles.GameRunning) throw new IOException("Close Valheim before exporting a consistent profile backup.");
        var mods = OwnedMods.Read(profile);
        string output = Path.GetFullPath(destination);
        string profileRoot = Path.GetFullPath(profile).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (output.StartsWith(profileRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Save the XML outside the profile folder.");
        var files = ModProfiles.SafeFiles(profile).Where(f => Included(Path.GetRelativePath(profile, f))).ToArray();
        if (files.Length > MaxFiles || files.Sum(f => new FileInfo(f).Length) > MaxBytes) throw new IOException("This profile exceeds the XML backup limit (16 GB / 100,000 files).");
        string temp = output + ".tmp-" + Guid.NewGuid().ToString("N");
        try {
            using (var writer = XmlWriter.Create(temp, new XmlWriterSettings { Indent = true, Encoding = new System.Text.UTF8Encoding(false) })) {
                writer.WriteStartElement("RuneModProfile"); writer.WriteAttributeString("version", "1");
                writer.WriteAttributeString("name", Path.GetFileName(profile.TrimEnd(Path.DirectorySeparatorChar)));
                writer.WriteAttributeString("exportedUtc", DateTime.UtcNow.ToString("O"));
                writer.WriteStartElement("Mods");
                foreach (var mod in mods) {
                    writer.WriteStartElement("Mod"); writer.WriteAttributeString("id", mod.Id); writer.WriteAttributeString("name", mod.Name);
                    writer.WriteAttributeString("version", mod.Version); writer.WriteAttributeString("enabled", XmlConvert.ToString(mod.Enabled));
                    foreach (string dependency in mod.Dependencies) writer.WriteElementString("Dependency", dependency);
                    foreach (string file in mod.Files) {
                        string physical = OwnedMods.SafePath(profile, file);
                        if (!Included(file)) throw new IOException("Unsupported mod file in this profile: " + file);
                        if (!mod.Enabled && !OwnedMods.IsConfig(file)) physical += ".old";
                        if (!File.Exists(physical)) throw new IOException("Repair or reinstall the mod before exporting; missing file: " + file);
                        writer.WriteElementString("Path", file.Replace('\\', '/'));
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement(); writer.WriteStartElement("Files");
                byte[] buffer = new byte[64 * 1024];
                foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) {
                    using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    string hash = Convert.ToHexString(SHA256.HashData(input)); input.Position = 0;
                    writer.WriteStartElement("File"); writer.WriteAttributeString("path", Path.GetRelativePath(profile, file).Replace('\\', '/'));
                    writer.WriteAttributeString("length", input.Length.ToString(CultureInfo.InvariantCulture)); writer.WriteAttributeString("sha256", hash);
                    int count; while ((count = input.Read(buffer, 0, buffer.Length)) > 0) writer.WriteBase64(buffer, 0, count);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement(); writer.WriteEndElement();
            }
            if (ModProfiles.GameRunning) throw new IOException("Valheim started during export. Close it and try again.");
            File.Move(temp, output, true);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static string Import(string root, string name, string source)
    {
        if (ModProfiles.GameRunning) throw new IOException("Close Valheim before importing a mod profile.");
        string target = OwnedMods.NewPath(root, name);
        Directory.CreateDirectory(root);
        string stage = OwnedMods.SafePath(root, ".xml-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try {
            using var reader = XmlReader.Create(source, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true, IgnoreComments = true,
                MaxCharactersInDocument = MaxBytes * 2, MaxCharactersFromEntities = 1024
            });
            reader.MoveToContent();
            if (reader.Name != "RuneModProfile" || reader.GetAttribute("version") != "1") throw new IOException("Choose a supported Rune XML mod profile (version 1).");
            reader.ReadStartElement("RuneModProfile"); reader.MoveToContent();
            bool emptyMods = reader.IsEmptyElement; reader.ReadStartElement("Mods");
            var mods = new List<OwnedMod>(); var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!emptyMods) {
                while (reader.MoveToContent() == XmlNodeType.Element && reader.Name == "Mod") {
                    var mod = (XElement)XNode.ReadFrom(reader);
                    string id = Required(mod, "id");
                    if (!ids.Add(id) || mods.Count >= 4096) throw new IOException("The XML contains duplicate or too many mods.");
                    string[] paths = mod.Elements("Path").Select(e => e.Value).ToArray();
                    if (paths.Length > MaxFiles) throw new IOException("Too many mod file entries.");
                    foreach (string path in paths) { _ = OwnedMods.SafePath(stage, path); if (!Included(path)) throw new IOException("Unsupported mod path: " + path); }
                    mods.Add(new(id, Required(mod, "name"), Required(mod, "version"), XmlConvert.ToBoolean(Required(mod, "enabled")), mod.Elements("Dependency").Select(e => e.Value).ToArray(), paths));
                }
                reader.ReadEndElement();
            }
            reader.MoveToContent(); bool emptyFiles = reader.IsEmptyElement; reader.ReadStartElement("Files");
            long total = 0; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); byte[] buffer = new byte[64 * 1024];
            if (!emptyFiles) {
                while (reader.MoveToContent() == XmlNodeType.Element && reader.Name == "File") {
                    string path = reader.GetAttribute("path") ?? throw new IOException("Missing file path.");
                    string destination = OwnedMods.SafePath(stage, path);
                    if (!Included(path) || !seen.Add(Path.GetRelativePath(stage, destination)) || seen.Count > MaxFiles) throw new IOException("Unsupported or duplicate file path: " + path);
                    if (!long.TryParse(reader.GetAttribute("length"), NumberStyles.None, CultureInfo.InvariantCulture, out long expected) || expected < 0 || expected > MaxBytes - total) throw new IOException("Invalid file length or XML backup size limit exceeded.");
                    string hash = reader.GetAttribute("sha256") ?? "";
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)) {
                        long written = 0; int count;
                        while ((count = reader.ReadElementContentAsBase64(buffer, 0, buffer.Length)) > 0) {
                            written += count;
                            if (written > expected) throw new IOException("File payload exceeds its declared length.");
                            output.Write(buffer, 0, count); digest.AppendData(buffer, 0, count);
                        }
                        if (written != expected || !Convert.ToHexString(digest.GetHashAndReset()).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("The XML backup is damaged: " + path);
                    }
                    total += expected;
                }
                reader.ReadEndElement();
            }
            reader.MoveToContent(); reader.ReadEndElement();
            if (reader.MoveToContent() != XmlNodeType.None) throw new IOException("Unexpected content after the profile.");
            foreach (var mod in mods) foreach (string path in mod.Files) {
                string physical = OwnedMods.SafePath(stage, path);
                // Disabled packages retain configs under their original names.
                if (!mod.Enabled && !OwnedMods.IsConfig(path)) physical += ".old";
                if (!File.Exists(physical)) throw new IOException("The XML is missing an installed mod file: " + path);
            }
            OwnedMods.Save(stage, mods); BlueprintProfiles.Prepare(stage, target);
            if (ModProfiles.GameRunning) throw new IOException("Valheim started during import. Close it and try again.");
            Directory.Move(stage, target); return target;
        } finally {
            // Only this operation's verified, newly created staging directory can be removed.
            if (Directory.Exists(stage)) { _ = OwnedMods.SafePath(root, Path.GetRelativePath(root, stage)); _ = ModProfiles.SafeFiles(stage).ToArray(); Directory.Delete(stage, true); }
        }
    }

    private static string Required(XElement element, string attribute) => (string?)element.Attribute(attribute) ?? throw new IOException("Missing mod " + attribute + ".");
}
