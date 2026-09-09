using System.Text.Json;
namespace Rune.Voice;

public sealed partial class RuneWindow
{
    internal static void TestManagement(string output)
    {
        ApplicationConfiguration.Initialize();
        Directory.CreateDirectory(output);
        string previewProfile = Path.Combine(output, "mod-profiles", "Fellowship"); Directory.CreateDirectory(previewProfile);
        OwnedMods.Save(previewProfile, new() {
            new("MathiasDecrock-PlanBuild", "PlanBuild", "0.18.4", true, new[] { "ValheimModding-Jotunn-2.28.0" }, new[] { "BepInEx/plugins/MathiasDecrock-PlanBuild/PlanBuild.dll" }),
            new("ValheimModding-Jotunn", "Jotunn", "2.29.2", true, Array.Empty<string>(), new[] { "BepInEx/plugins/ValheimModding-Jotunn/Jotunn.dll" }),
            new("RuneCompanion", "Rune Fellowship", "0.3.10", true, new[] { "ValheimModding-Jotunn-2.28.0" }, new[] { "BepInEx/plugins/RuneCompanion/RuneCompanion.dll" }) });
        using var window = new RuneWindow(Path.Combine(output, "isolated-bridge"));
        window.Opacity = 0; window.ShowInTaskbar = false;
        window.Shown += async (_, _) => {
            var checks = new List<string>();
            try {
                window.timer.Stop(); window.microphoneMode.SelectedIndex = 0;
                var first = window.preferences.Profiles[0]; var second = window.preferences.Profiles[1];
                window.SelectCompanion(first.Id);
                await window.Submit("Hey " + second.Name + ", follow me", true, Guid.NewGuid().ToString());
                if (window.bridge.CompanionId != first.Id || Directory.GetFiles(window.bridge.Folder, "command*.json").Length > 0) throw new Exception("Speech reached another companion");
                checks.Add("PASS: Addressing another companion by voice does not select or dispatch to them.");
                window.heardSpeech.Enqueue(new HeardSpeech("gather wood", "test", DateTime.UtcNow));
                int before = window.recipientVersion; window.SelectCompanion(second.Id);
                if (window.heardSpeech.Count != 0 || window.recipientVersion <= before) throw new Exception("Switch left queued/old capture speech active");
                checks.Add("PASS: Selecting another companion clears pending speech and invalidates in-flight recognition.");
                window.preferences.MicShortcut = "Ctrl + Shift + R"; window.preferences.SwitchCompanionShortcut = "F10"; window.SavePreferences();
                var loaded = ProfileStore.Load(window.settingsPath);
                if (loaded.MicShortcut != "Ctrl + Shift + R" || loaded.SwitchCompanionShortcut != "F10") throw new Exception("Keybinds did not persist");
                checks.Add("PASS: Microphone and companion-switch keybinds survive reload.");
                foreach (string page in new[] { "keybinds", "mods" }) {
                    window.ClientSize = new Size(1354, 849); window.ShowPage(page); window.PerformLayout();
                    using var bitmap = new Bitmap(window.Width, window.Height); window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size)); bitmap.Save(Path.Combine(output, page + ".png"));
                }
                window.ClientSize = new Size(1180, 800); window.ShowPage("mods"); window.PerformLayout();
                using (var bitmap = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size)); bitmap.Save(Path.Combine(output, "mods-small.png")); }
                string profile = Path.Combine(output, "toggle-fixture"), folder = Path.Combine(profile, "BepInEx", "plugins", "TestMod"); Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "test.dll"), "synthetic library"); File.WriteAllText(Path.Combine(folder, "asset.bundle"), "synthetic asset");
                OwnedMods.Save(profile, new() { new("TestMod", "Test mod", "1.0.0", true, Array.Empty<string>(), new[] { "BepInEx/plugins/TestMod/test.dll", "BepInEx/plugins/TestMod/asset.bundle" }) });
                ModProfiles.Toggle(profile, "TestMod");
                if (File.Exists(Path.Combine(folder, "test.dll")) || !File.Exists(Path.Combine(folder, "asset.bundle.old"))) throw new Exception("Disable failed");
                ModProfiles.Toggle(profile, "TestMod");
                if (File.ReadAllText(Path.Combine(folder, "test.dll")) != "synthetic library" || !File.Exists(Path.Combine(folder, "asset.bundle"))) throw new Exception("Enable failed or changed bytes");
                checks.Add("PASS: Isolated mod toggle renames/restores plugin and asset files without changing content.");
                var core = new InstalledMod("author-Core", "Core", "1.0.0", true, Array.Empty<string>(), new[] { folder }, "manifest");
                var consumer = new InstalledMod("author-Consumer", "Consumer", "1.0.0", true, new[] { "author-Core-1.0.0" }, new[] { folder }, "manifest");
                if (ModProfiles.DependencyError(new() { core, consumer }, core, false).Length == 0 || ModProfiles.DependencyError(new() { consumer }, consumer, true).Length == 0) throw new Exception("Dependency safeguards failed");
                checks.Add("PASS: Cannot disable a required library or enable a mod with a missing dependency.");
                if (ModProfiles.DependencyError(new() { core with { Version = "0.9.0" }, consumer }, consumer, true).Length == 0) throw new Exception("Outdated dependency accepted");
                string manifest = Path.Combine(output, "manifest-fixture"); Directory.CreateDirectory(manifest);
                File.WriteAllText(Path.Combine(manifest, "mods.yml"), "- manifestVersion: 1\n  name: author-Core\n  displayName: Core\n  dependencies: []\n  versionNumber:\n    major: 1\n    minor: 2\n    patch: 3\n  enabled: true\n- manifestVersion: 1\n  name: author-Consumer\n  displayName: Consumer\n  dependencies:\n    - author-Core-1.2.3\n  versionNumber:\n    major: 2\n    minor: 0\n    patch: 0\n  enabled: false\n");
                var parsed = ModProfiles.Read(manifest);
                if (parsed.Count != 2 || parsed[0].Version != "1.2.3" || !parsed[0].Enabled || parsed[1].Enabled || parsed[1].Dependencies.Single() != "author-Core-1.2.3") throw new Exception("r2modman metadata parse failed");
                checks.Add("PASS: r2modman manifest names, versions, state and dependency list are read; outdated dependencies are rejected.");
                string fakeGame = Path.Combine(output, "valheim.exe"); File.WriteAllText(fakeGame, "not executed"); string preloader = Path.Combine(profile, "BepInEx", "core"); Directory.CreateDirectory(preloader); File.WriteAllText(Path.Combine(preloader, "BepInEx.Preloader.dll"), "not executed");
                var launch = ModProfiles.LaunchInfo(fakeGame, profile, true);
                if (!launch.ArgumentList.Contains(Path.Combine(preloader, "BepInEx.Preloader.dll")) || !ModProfiles.LaunchInfo(fakeGame, profile, false).ArgumentList.Contains("false")) throw new Exception("Profile launch arguments incorrect");
                checks.Add("PASS: Launch arguments target the selected profile; vanilla explicitly disables injection. No game launched by this check.");
            } catch (Exception e) { checks.Add("FAILED: " + e); }
            File.WriteAllLines(Path.Combine(output, "report.txt"), checks); window.Close();
        };
        Application.Run(window);
    }
}
