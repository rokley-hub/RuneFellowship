using System.IO.Compression;
using System.Text;
namespace Rune.Voice;
internal static class OwnedModChecks
{
    internal static byte[] Package(string name, string version, params (string Path, string Text)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write("{\"name\":\"" + name + "\",\"version_number\":\"" + version + "\"}");
            foreach (var file in files) using (var writer = new StreamWriter(zip.CreateEntry(file.Path).Open())) writer.Write(file.Text);
        }
        return memory.ToArray();
    }
    internal static void Run(string root)
    {
        Directory.CreateDirectory(root); var report = new List<string>();
        void Check(bool value, string message) { if (!value) throw new Exception(message); report.Add("PASS: " + message); }
        try {
            string profile = OwnedMods.Create(root, "Package test " + Guid.NewGuid().ToString("N")[..6]);
            var catalog = new CatalogMod("Fixture-Test", "Test", "1.0.0", "", Array.Empty<string>(), "", "", false);
            var package = Package("Test", "1.0.0", ("plugins/Test.dll", "original dll"), ("plugins/assets/wood.bundle", "asset"), ("config/test.cfg", "original setting"));
            OwnedMods.Transaction(profile, stage => OwnedMods.Save(stage, new() { OwnedMods.InstallArchive(stage, catalog, package, new()) }));
            Check(File.ReadAllText(Path.Combine(profile, "BepInEx/plugins/Fixture-Test/assets/wood.bundle")) == "asset", "Archive preserves nested plugin assets and maps config separately.");
            OwnedMods.Toggle(profile, catalog.Id);
            Check(File.Exists(Path.Combine(profile, "BepInEx/plugins/Fixture-Test/Test.dll.old")) && File.Exists(Path.Combine(profile, "BepInEx/config/test.cfg")), "Disable removes plugin and assets from loading while keeping configuration.");
            OwnedMods.Toggle(profile, catalog.Id);
            Check(File.ReadAllText(Path.Combine(profile, "BepInEx/plugins/Fixture-Test/Test.dll")) == "original dll", "Enable restores the original plugin bytes.");
            try { OwnedMods.Transaction(profile, stage => { File.WriteAllText(Path.Combine(stage, "BepInEx/config/test.cfg"), "corrupted"); throw new IOException("Simulated failure"); }); } catch (IOException) { }
            Check(File.ReadAllText(Path.Combine(profile, "BepInEx/config/test.cfg")) == "original setting", "A failure after staging changes leaves the active profile untouched.");
            foreach (string unsafePath in new[] { "../escaped.dll", "plugins/../../escaped.dll", "C:/escaped.dll", "plugins/test.dll:stream" }) {
                bool blocked = false;
                try { OwnedMods.Transaction(profile, stage => OwnedMods.InstallArchive(stage, catalog, Package("Test", "1.0.0", (unsafePath, "bad")), new())); } catch (IOException) { blocked = true; }
                Check(blocked, "Rejects unsafe archive path " + unsafePath);
            }
            OwnedMods.SaveConfig(profile, "BepInEx/config/test.cfg", "user setting");
            File.WriteAllText(Path.Combine(profile, "BepInEx/config/unrelated.cfg"), "other mod setting");
            Check(OwnedMods.ConfigFilesFor(profile, catalog.Id).SequenceEqual(new[] { "BepInEx/config/test.cfg" }, StringComparer.OrdinalIgnoreCase), "Selected-mod configuration never falls through to another mod's settings file.");
            var v2 = catalog with { Version = "2.0.0" };
            OwnedMods.Transaction(profile, stage => {
                var existing = OwnedMods.Read(stage).Single();
                foreach (string file in existing.Files.Where(f => !OwnedMods.IsConfig(f))) File.Delete(OwnedMods.SafePath(stage, file));
                var updated = OwnedMods.InstallArchive(stage, v2, Package("Test", "2.0.0", ("Test.dll", "updated dll"), ("config/test.cfg", "package default")), new()); OwnedMods.Save(stage, new() { updated });
            });
            Check(File.ReadAllText(Path.Combine(profile, "BepInEx/config/test.cfg")) == "user setting" && !File.Exists(Path.Combine(profile, "BepInEx/plugins/Fixture-Test/assets/wood.bundle")), "Update preserves user settings and removes obsolete owned files.");
            OwnedMods.RestoreLast(profile);
            Check(OwnedMods.Read(profile).Single().Version == "1.0.0" && File.ReadAllText(Path.Combine(profile, "BepInEx/plugins/Fixture-Test/Test.dll")) == "original dll", "Restore backup recovers the preceding complete profile.");
            string copy = OwnedMods.Import(root, "Imported " + Guid.NewGuid().ToString("N")[..6], profile);
            OwnedMods.Toggle(copy, catalog.Id);
            Check(OwnedMods.Read(profile).Single().Enabled && !OwnedMods.Read(copy).Single().Enabled, "Imported profile is independent: toggling the copy leaves the source enabled.");
            var dependency = catalog with { Id = "Fixture-Core", Name = "Core" };
            var consumer = catalog with { Dependencies = new[] { "Fixture-Core-1.0.0" } };
            var plan = ModCatalog.Resolve(new[] { consumer }, new() { dependency, consumer }, new());
            Check(plan.Count == 2 && plan[0].Id == dependency.Id, "Dependency closure installs required libraries before the requested mod.");
            Check(ModCatalog.IsNewer("1.2.3", "1.2.4") && !ModCatalog.IsNewer("2.0.0", "1.9.9") && !ModCatalog.IsNewer("Local", "2.0.0"), "Update detection compares valid package versions without flagging local-only mods.");
            var bepinex = catalog with { Id = "denikson-BepInExPack_Valheim", Name = "BepInEx" };
            var jotunn = catalog with { Id = "ValheimModding-Jotunn", Name = "Jotunn", Dependencies = new[] { "denikson-BepInExPack_Valheim-1.0.0" } };
            var foundationPlan = ModCatalog.WithProfileFoundation(new[] { consumer }, new() { bepinex, jotunn, consumer }, Array.Empty<InstalledMod>());
            Check(foundationPlan.Select(m => m.Id).ToHashSet().SetEquals(new[] { bepinex.Id, jotunn.Id, consumer.Id }), "The first mod added to a fresh Rune profile also requests BepInEx and Jötunn.");
            var core = bepinex with { Version = "5.4.2333" };
            var library = jotunn with { Version = "2.29.2", Dependencies = new[] { "denikson-BepInExPack_Valheim-5.4.2333" } };
            var hook = catalog with { Id = "ValheimModding-HookGenPatcher", Name = "HookGenPatcher", Version = "0.0.4", Dependencies = new[] { "denikson-BepInExPack_Valheim-5.4.2333" } };
            var building = catalog with { Id = "MathiasDecrock-PlanBuild", Name = "PlanBuild", Version = "0.18.4", Dependencies = new[] { "ValheimModding-Jotunn-2.28.0", "ValheimModding-HookGenPatcher-0.0.4" } };
            var requirementCatalog = new List<CatalogMod> { core, library, hook, building };
            var requirements = ModCatalog.RuneRequirements(requirementCatalog, new());
            var fullPlan = ModCatalog.Resolve(requirements, requirementCatalog, new());
            Check(fullPlan.Count == 4 && fullPlan[0].Id == core.Id && fullPlan[^1].Id == building.Id, "One setup request includes BepInEx, Jötunn, PlanBuild and transitive HookGenPatcher dependencies in install order.");
            bool incomplete = false;
            try { ModCatalog.RuneRequirements(new() { core, library }, new()); } catch (IOException) { incomplete = true; }
            Check(incomplete, "Unavailable requirements fail before downloading or changing a profile.");
            string requirementsProfile = OwnedMods.Create(root, "Requirements fixture");
            var installedRequirements = new List<OwnedMod>();
            foreach (var requirement in fullPlan) {
                string relative = "BepInEx/plugins/" + requirement.Id + "/fixture.dll";
                string physical = OwnedMods.SafePath(requirementsProfile, relative + ".old"); Directory.CreateDirectory(Path.GetDirectoryName(physical)!); File.WriteAllText(physical, "synthetic dependency");
                installedRequirements.Add(new(requirement.Id, requirement.Name, requirement.Version, false, requirement.Dependencies, new[] { relative }));
            }
            OwnedMods.Save(requirementsProfile, installedRequirements);
            var reuse = ModCatalog.RuneRequirements(new(), installedRequirements);
            Check(reuse.All(m => m.Download == ""), "Setup reuses compatible installed versions without forced updates or downloads.");
            OwnedMods.Install(requirementsProfile, reuse, new(), new Progress<string>(), CancellationToken.None, enableRequested: true).GetAwaiter().GetResult();
            Check(OwnedMods.Read(requirementsProfile).All(m => m.Enabled) && installedRequirements.All(m => File.Exists(OwnedMods.SafePath(requirementsProfile, m.Files[0]))), "Explicit requirements setup enables disabled roots and their dependencies without downloading or losing files.");
            var oldLibrary = installedRequirements.Select(m => m.Id == library.Id ? m with { Version = "2.0.0" } : m).ToList();
            Check(ModCatalog.RuneRequirements(requirementCatalog, oldLibrary).Single(m => m.Id == library.Id).Version == library.Version, "Setup replaces a library below Rune's minimum version while keeping compatible packages.");
            bool missing = false; try { ModCatalog.Resolve(new[] { consumer }, new() { consumer }, new()); } catch (IOException) { missing = true; }
            Check(missing, "Missing dependencies fail before any profile change.");
            OwnedMods.Remove(profile, catalog.Id);
            Check(!File.Exists(Path.Combine(profile, "BepInEx/plugins/Fixture-Test/Test.dll")) && File.ReadAllText(Path.Combine(profile, "BepInEx/config/test.cfg")) == "user setting", "Uninstall removes owned files but retains user settings.");
            string runeDll = Path.Combine(root, "synthetic-rune.dll"); File.WriteAllText(runeDll, "synthetic Rune plugin");
            OwnedMods.AddRune(profile, Path.Combine(root, "bridge"), runeDll);
            var profileMods = OwnedMods.Read(profile); var rune = profileMods.Single();
            // Provide the required library for the toggle check, without executing it.
            profileMods.Add(new("ValheimModding-Jotunn", "Jotunn", "2.29.2", true, Array.Empty<string>(), Array.Empty<string>())); OwnedMods.Save(profile, profileMods);
            OwnedMods.Toggle(profile, "RuneCompanion"); OwnedMods.AddRune(profile, Path.Combine(root, "bridge"), runeDll);
            Check(!OwnedMods.Read(profile).Single(m => m.Id == "RuneCompanion").Enabled && File.Exists(Path.Combine(profile, "BepInEx/plugins/RuneCompanion/RuneCompanion.dll.old")), "Updating Rune preserves an intentionally disabled plugin.");
            string removable = OwnedMods.Create(root, "Removable " + Guid.NewGuid().ToString("N")[..6]);
            string removedBackup = OwnedMods.RemoveProfile(root, removable);
            Check(!Directory.Exists(removable) && Directory.Exists(removedBackup) && File.Exists(Path.Combine(removedBackup, OwnedMods.Manifest)), "Removing a profile takes it out of the launcher and keeps a recoverable backup.");
        } catch (Exception e) { report.Add("FAILED: " + e); Environment.ExitCode = 1; }
        File.WriteAllLines(Path.Combine(root, "report.txt"), report);
    }
}
