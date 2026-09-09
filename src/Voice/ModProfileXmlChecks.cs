using System.Security.Cryptography;
using System.Xml.Linq;
namespace Rune.Voice;

internal static class ModProfileXmlChecks
{
    internal static void Run(string root, string? existingProfile = null)
    {
        Directory.CreateDirectory(root); var report = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); report.Add("PASS: " + message); }
        try {
            string profiles = Path.Combine(root, "profiles"), source = OwnedMods.Create(profiles, "Source");
            var binaries = new byte[200003]; new Random(17).NextBytes(binaries);
            void Put(string path, byte[] bytes) { string file = OwnedMods.SafePath(source, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, bytes); }
            Put("BepInEx/plugins/Test/Test.dll.old", binaries);
            Put("BepInEx/plugins/Test/empty.bundle.old", Array.Empty<byte>());
            Put("BepInEx/config/Test.cfg", System.Text.Encoding.UTF8.GetBytes("[Custom]\nName = Ægir & friends <hello>\n"));
            Put("blueprints/Hall.blueprint", System.Text.Encoding.UTF8.GetBytes("fixture blueprint"));
            Put("BepInEx/cache/cached.dll", new byte[] { 1 }); Put("BepInEx/LogOutput.log", new byte[] { 2 });
            Put("preferences.json", new byte[] { 3 });
            OwnedMods.Save(source, new() { new("Fixture-Test", "Test & Ægir", "1.2.3", false, new[] { "Fixture-Core-1.0.0" }, new[] { "BepInEx/plugins/Test/Test.dll", "BepInEx/plugins/Test/empty.bundle", "BepInEx/config/Test.cfg" }) });
            string xml = Path.Combine(root, "profile.xml"); ModProfileXml.Export(source, xml);
            string imported = ModProfileXml.Import(profiles, "Restored", xml);
            var restored = OwnedMods.Read(imported).Single();
            Check(restored.Id == "Fixture-Test" && !restored.Enabled && restored.Name == "Test & Ægir" && restored.Version == "1.2.3" && restored.Dependencies.SequenceEqual(new[] { "Fixture-Core-1.0.0" }), "XML preserves package metadata, Unicode and disabled state.");
            Check(File.ReadAllBytes(Path.Combine(imported, "BepInEx/plugins/Test/Test.dll.old")).SequenceEqual(binaries) && new FileInfo(Path.Combine(imported, "BepInEx/plugins/Test/empty.bundle.old")).Length == 0, "Binary payloads larger than the stream buffer and empty files round-trip exactly.");
            Check(File.ReadAllText(Path.Combine(imported, "BepInEx/config/Test.cfg")) == File.ReadAllText(Path.Combine(source, "BepInEx/config/Test.cfg")) && File.Exists(Path.Combine(imported, "blueprints/Hall.blueprint")), "Custom configuration and local blueprints are restored.");
            Check(File.ReadAllText(Path.Combine(imported, "BepInEx/config/marcopogo.PlanBuild.cfg")).Contains(Path.Combine(imported, "blueprints").Replace('\\', '/')), "PlanBuild library paths point to the imported profile.");
            Check(!Directory.Exists(Path.Combine(imported, "BepInEx/cache")) && !File.Exists(Path.Combine(imported, "BepInEx/LogOutput.log")) && !File.Exists(Path.Combine(imported, "preferences.json")), "Cache, logs and app preferences are excluded.");
            File.WriteAllText(Path.Combine(imported, "BepInEx/config/Test.cfg"), "independent");
            Check(File.ReadAllText(Path.Combine(source, "BepInEx/config/Test.cfg")).Contains("Ægir"), "Imported files are independent from their source.");
            bool collision = false; try { ModProfileXml.Import(profiles, "Restored", xml); } catch (IOException) { collision = true; }
            Check(collision && File.ReadAllText(Path.Combine(imported, "BepInEx/config/Test.cfg")) == "independent", "Import refuses to overwrite an existing profile.");
            void Reject(string label, Action<XDocument> mutate) {
                var doc = XDocument.Load(xml); mutate(doc); string bad = Path.Combine(root, "bad.xml"); doc.Save(bad);
                bool failed = false; try { ModProfileXml.Import(profiles, "Rejected", bad); } catch (Exception ex) when (ex is IOException or System.Xml.XmlException or FormatException) { failed = true; }
                Check(failed && !Directory.Exists(Path.Combine(profiles, "Rejected")) && !Directory.GetDirectories(profiles).Any(d => Path.GetFileName(d).StartsWith(".xml-import-")), label + " is rejected without publishing or leaving staging files.");
            }
            Reject("Path traversal", d => d.Root!.Element("Files")!.Element("File")!.SetAttributeValue("path", "../escape.dll"));
            Reject("Duplicate payload", d => { var f = d.Root!.Element("Files")!; f.Add(new XElement(f.Element("File")!)); });
            Reject("Hash mismatch", d => d.Root!.Element("Files")!.Element("File")!.SetAttributeValue("sha256", "bad"));
            Reject("Missing tracked plugin", d => d.Root!.Element("Files")!.Elements("File").First(f => ((string?)f.Attribute("path"))!.EndsWith("Test.dll.old")).Remove());
            Reject("Invalid declared size", d => d.Root!.Element("Files")!.Element("File")!.SetAttributeValue("length", "999999999999999"));
            Reject("Unsupported version", d => d.Root!.SetAttributeValue("version", "999"));
            string dtd = Path.Combine(root, "dtd.xml"); File.WriteAllText(dtd, "<!DOCTYPE RuneModProfile [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><RuneModProfile version='1'><Mods>&x;</Mods><Files/></RuneModProfile>");
            bool dtdBlocked = false; try { ModProfileXml.Import(profiles, "DTD", dtd); } catch (System.Xml.XmlException) { dtdBlocked = true; }
            Check(dtdBlocked && !Directory.Exists(Path.Combine(profiles, "DTD")), "External entities and DTDs are prohibited.");
            string empty = OwnedMods.Create(profiles, "Empty"); string emptyXml = Path.Combine(root, "empty.xml"); ModProfileXml.Export(empty, emptyXml);
            Check(OwnedMods.Read(ModProfileXml.Import(profiles, "Empty copy", emptyXml)).Count == 0, "A new profile with no packages can round-trip.");
            if (existingProfile != null) {
                string existingXml = Path.Combine(root, "installed-profile.xml"); ModProfileXml.Export(existingProfile, existingXml);
                string existingCopy = ModProfileXml.Import(profiles, "Installed copy", existingXml);
                var originals = OwnedMods.Read(existingProfile); var copies = OwnedMods.Read(existingCopy);
                Check(originals.Count == copies.Count && originals.Select(m => (m.Id, m.Version, m.Enabled)).SequenceEqual(copies.Select(m => (m.Id, m.Version, m.Enabled))), "Installed profile restores every package version and enabled state into an isolated copy.");
                foreach (var mod in originals) foreach (string file in mod.Files) {
                    string physical = !mod.Enabled && !OwnedMods.IsConfig(file) ? file + ".old" : file;
                    if (physical.Replace('\\', '/').Equals("BepInEx/config/marcopogo.PlanBuild.cfg", StringComparison.OrdinalIgnoreCase)) continue;
                    using var before = File.OpenRead(OwnedMods.SafePath(existingProfile, physical)); using var after = File.OpenRead(OwnedMods.SafePath(existingCopy, physical));
                    if (!SHA256.HashData(before).SequenceEqual(SHA256.HashData(after))) throw new Exception("Installed profile file changed during round trip: " + physical);
                }
                Check(true, "Installed plugin, asset and configuration bytes match after export/import; PlanBuild path relocation is intentional.");
            }
        } catch (Exception ex) { report.Add("FAILED: " + ex); Environment.ExitCode = 1; }
        File.WriteAllLines(Path.Combine(root, "report.txt"), report);
    }
}
