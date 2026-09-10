using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rune.Voice;

internal sealed record RuneUpdate(string Version, string Tag, string Download, string Checksum, string Notes);
internal sealed record UpdateFile(string path, long bytes, string sha256);
internal sealed record UpdateManifest(int format, string desktop, string gameplay, UpdateFile[] files);
internal static class RuneUpdates
{
    internal const string Repository = "https://github.com/rokley-hub/RuneFellowship";
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    internal static string Hash(string path) { using var f = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }
    internal static Version Number(string value)
    {
        if (!Regex.IsMatch(value, @"^v?\d+\.\d+\.\d+(-beta)?$")) throw new IOException("Unsupported Rune release version.");
        return Version.Parse(value.TrimStart('v').Split('-')[0]);
    }
    internal static string Current(string root) => JsonNode.Parse(File.ReadAllText(Path.Combine(root, "release.json")))!["version"]!.GetValue<string>();
    internal static RuneUpdate? Select(string releases, string current)
    {
        using var doc = JsonDocument.Parse(releases);
        var found = new List<RuneUpdate>();
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            if (release.GetProperty("prerelease").GetBoolean() && !current.Contains("-beta")) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+(-beta)?$")) continue;
            if (Number(tag) <= Number(current)) continue;
            string name = "Rune-Update-" + tag[1..] + ".zip";
            string prefix = Repository + "/releases/download/" + tag + "/";
            var assets = release.GetProperty("assets").EnumerateArray().ToArray();
            bool Has(string n) => assets.Any(a => a.GetProperty("name").GetString() == n && a.GetProperty("browser_download_url").GetString() == prefix + n);
            if (Has(name) && Has(name + ".sha256")) found.Add(new(tag[1..], tag, prefix + name, prefix + name + ".sha256", Repository + "/releases/tag/" + tag));
        }
        return found.OrderByDescending(f => Number(f.Version)).FirstOrDefault();
    }
    internal static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RuneFellowship-Updater/1.0");
        return client;
    }
    internal static async Task<RuneUpdate?> Check(string root, CancellationToken token)
    {
        using var client = Client();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        return Select(await client.GetStringAsync("https://api.github.com/repos/rokley-hub/RuneFellowship/releases?per_page=30", timeout.Token), Current(root));
    }
    internal static string Safe(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.EndsWith('.') || p.EndsWith(' ') || Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase))) throw new IOException("Unsafe update path.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(parts)));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Update path leaves the installation.");
        for (string? p = path; p != null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked update folders are unsupported.");
        return path;
    }
    internal static bool Allowed(string path) => path.StartsWith("app/", StringComparison.Ordinal) || path.StartsWith("licenses/", StringComparison.Ordinal) || path.StartsWith("docs/", StringComparison.Ordinal) || path == "payload/BepInEx/plugins/RuneCompanion/RuneCompanion.dll" || new[] { "release.json", "Rune.exe", "Open Rune.exe", "Start Rune.ps1", "Stop Rune services.ps1", "Service Ports.ps1", "Verify Rune.ps1", "Rollback Rune.ps1", "Update Recovery.ps1", "Uninstall Rune.ps1", "package_tools.py", "LICENSE", "LINKING-EXCEPTION.md", "runtime/audio-service.py", "runtime/chatterbox-service.py", "runtime/voice_worker.py" }.Contains(path);
    internal static UpdateManifest Validate(string stage)
    {
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(Safe(stage, "update.json"))) ?? throw new IOException("Missing update manifest.");
        if (manifest.format != 1 || manifest.files.Length is < 3 or > 10000) throw new IOException("Unsupported update package.");
        Number(manifest.desktop); Number(manifest.gameplay);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var f in manifest.files)
        {
            if (!Allowed(f.path) || !paths.Add(f.path) || f.bytes < 0 || (total += f.bytes) > 1_000_000_000 || !Regex.IsMatch(f.sha256, "^[a-f0-9]{64}$")) throw new IOException("Invalid update file manifest.");
            string p = Safe(stage, f.path);
            if (!File.Exists(p) || new FileInfo(p).Length != f.bytes || Hash(p) != f.sha256) throw new IOException("Update verification failed: " + f.path);
        }
        foreach (string required in new[] { "app/RuneVoice.dll", "app/RuneVoice.runtimeconfig.json", "release.json", "payload/BepInEx/plugins/RuneCompanion/RuneCompanion.dll" })
            if (!paths.Contains(required)) throw new IOException("Update is missing the app or matching game plugin.");
        var release = JsonNode.Parse(File.ReadAllText(Safe(stage, "release.json")))!;
        if (release["desktop"]!.GetValue<string>() != manifest.desktop || release["gameplay"]!.GetValue<string>() != manifest.gameplay || Number(release["version"]!.GetValue<string>()) != Number(manifest.desktop)) throw new IOException("App and plugin release metadata do not match.");
        return manifest;
    }
    internal static void Extract(string archive, string stage)
    {
        Directory.CreateDirectory(stage);
        using var zip = ZipFile.OpenRead(archive);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        if (zip.Entries.Count > 10001) throw new IOException("Update archive has too many files.");
        foreach (var entry in zip.Entries)
        {
            if (entry.Name.Length == 0) continue;
            if (!seen.Add(entry.FullName) || (total += entry.Length) > 1_000_000_000 || (entry.FullName != "update.json" && !Allowed(entry.FullName))) throw new IOException("Invalid update archive.");
            string path = Safe(stage, entry.FullName); Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path);
        }
        var manifest = Validate(stage);
        if (seen.Count != manifest.files.Length + 1) throw new IOException("Update contains unlisted files.");
    }
    internal static async Task<string> Download(RuneUpdate update, IProgress<string> progress, CancellationToken token)
    {
        using var client = Client();
        string hashText = (await client.GetStringAsync(update.Checksum, token)).Trim();
        string expected = hashText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (!Regex.IsMatch(expected, "^[a-fA-F0-9]{64}$")) throw new IOException("Release checksum is missing or invalid.");
        string folder = Path.Combine(Path.GetTempPath(), "Rune-Update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string archive = Path.Combine(folder, "download.zip"), stage = Path.Combine(folder, "stage");
        using (var response = await client.GetAsync(update.Download, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 256_000_000) throw new IOException("Update download is too large.");
            using var input = await response.Content.ReadAsStreamAsync(token);
            using var output = File.Create(archive); byte[] buffer = new byte[131072]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer, token)) > 0)
            {
                total += read; if (total > 256_000_000) throw new IOException("Update download is too large.");
                await output.WriteAsync(buffer.AsMemory(0, read), token); progress.Report($"Downloading Rune {update.Version} · {total / 1048576} MB");
            }
        }
        progress.Report("Verifying update…");
        if (!Hash(archive).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("The update checksum did not match. Nothing was installed.");
        await Task.Run(() => Extract(archive, stage), token);
        if (Number(Validate(stage).desktop) != Number(update.Version) || Current(stage) != update.Version) throw new IOException("Downloaded release version does not match.");
        return stage;
    }
    internal static bool GameRunning() => Process.GetProcessesByName("valheim").Length > 0;
    private sealed record Change(string Path, string? Source, byte[]? Content);
    private sealed record Saved(string Path, bool Existed, string? Previous);
    internal static void Apply(string stage, string root, Action? guard = null, Action<int>? afterWrite = null)
    {
        stage = Path.GetFullPath(stage); root = Path.GetFullPath(root);
        if (stage.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || root.StartsWith(stage.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || stage.Equals(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Update must be staged outside the installation.");
        var manifest = Validate(stage);
        if (Number(manifest.desktop) <= Number(Current(root))) throw new IOException("This update is not newer than the installed app.");
        var changes = manifest.files.Select(f => new Change(f.path, Safe(stage, f.path), null)).ToList();
        string dll = Safe(stage, "payload/BepInEx/plugins/RuneCompanion/RuneCompanion.dll");
        // Only Rune-owned profiles and Rune's own DLL are changed. Preserve configs, other mods and disabled state.
        foreach (string profile in OwnedMods.Profiles(Safe(root, "mod-profiles")))
        {
            var mods = OwnedMods.Read(profile); int i = mods.FindIndex(m => m.Id == "RuneCompanion"); if (i < 0) continue;
            var mod = mods[i]; const string canonical = "BepInEx/plugins/RuneCompanion/RuneCompanion.dll";
            if (mod.Files.Length != 1 || mod.Files[0] != canonical) throw new IOException("Rune plugin layout is unexpected in " + Path.GetFileName(profile));
            string prefix = Path.GetRelativePath(root, profile).Replace('\\', '/') + "/";
            changes.Add(new(prefix + canonical + (mod.Enabled ? "" : ".old"), dll, null));
            mods[i] = mod with { Version = manifest.gameplay };
            changes.Add(new(prefix + OwnedMods.Manifest, null, JsonSerializer.SerializeToUtf8Bytes(mods, Json)));
        }
        // Merge ownership with installed runtime/model receipts so verification and uninstall still know those files.
        var installed = JsonNode.Parse(File.ReadAllText(Safe(root, "files.json")))!;
        var rows = installed["files"]!.AsArray();
        foreach (var f in manifest.files)
        {
            foreach (var old in rows.Where(n => string.Equals(n!["path"]!.GetValue<string>(), f.path, StringComparison.OrdinalIgnoreCase)).ToArray()) rows.Remove(old);
            rows.Add(JsonSerializer.SerializeToNode(f));
        }
        installed["release"] = Current(stage);
        changes.Add(new("files.json", null, JsonSerializer.SerializeToUtf8Bytes(installed, Json)));
        foreach (var change in changes) Safe(root, change.Path);
        guard?.Invoke();
        string backup = Safe(root, "release-backups/update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(backup);
        var saved = new List<Saved>();
        try
        {
            // Back up every affected file before the first write.
            foreach (var change in changes)
            {
                string dest = Safe(root, change.Path); bool existed = File.Exists(dest);
                if (existed) { string b = Safe(backup, change.Path); Directory.CreateDirectory(Path.GetDirectoryName(b)!); File.Copy(dest, b); }
                saved.Add(new(change.Path, existed, existed ? Hash(Safe(backup, change.Path)) : null));
            }
            File.WriteAllText(Path.Combine(backup, "update-transaction.json"), JsonSerializer.Serialize(saved, Json));
            int count = 0;
            foreach (var change in changes)
            {
                guard?.Invoke(); string dest = Safe(root, change.Path); Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                string temp = dest + ".rune-update-" + Guid.NewGuid().ToString("N");
                try { if (change.Source != null) File.Copy(change.Source, temp); else File.WriteAllBytes(temp, change.Content!); File.Move(temp, dest, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                afterWrite?.Invoke(++count);
            }
            File.WriteAllText(Path.Combine(backup, "COMPLETED.txt"), "Updated to " + manifest.desktop);
        }
        catch
        {
            foreach (var old in saved.AsEnumerable().Reverse())
            {
                string dest = Safe(root, old.Path);
                if (old.Existed) File.Copy(Safe(backup, old.Path), dest, true); else if (File.Exists(dest)) File.Delete(dest);
            }
            File.WriteAllText(Path.Combine(backup, "ROLLED-BACK.txt"), "Previous files restored.");
            throw;
        }
    }
    internal static ProcessStartInfo Worker(string stage, string root)
    {
        string local = Safe(root, "runtime/dotnet/dotnet.exe");
        var info = new ProcessStartInfo(File.Exists(local) ? local : "dotnet") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = stage };
        foreach (var a in new[] { Safe(stage, "app/RuneVoice.dll"), "--apply-rune-update", root, Environment.ProcessId.ToString() }) info.ArgumentList.Add(a);
        return info;
    }
    internal static void RunWorker(string root, int parent)
    {
        string stage = Root;
        try
        {
            try { using var process = Process.GetProcessById(parent); if (!process.WaitForExit(90000)) throw new IOException("Rune is still open. Close it before retrying the update."); } catch (ArgumentException) { }
            using var mutex = new Mutex(true, "RuneVoice_Local_01", out bool alone);
            if (!alone) throw new IOException("Another Rune window is open. Close it before updating.");
            string key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToLowerInvariant())));
            using var instance = new Mutex(false, "Local\\RuneFellowship-" + key);
            bool acquired;
            try { acquired = instance.WaitOne(TimeSpan.FromSeconds(90)); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Rune is still shutting down. Retry the update once it has closed.");
            void Guard() { if (GameRunning()) throw new IOException("Valheim is running. Close the game, then retry the update."); }
            Guard();
            var stop = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Safe(root, "Stop Rune services.ps1") }) stop.ArgumentList.Add(arg);
            using (var services = Process.Start(stop)!) { if (!services.WaitForExit(30000) || services.ExitCode != 0) throw new IOException("Rune services did not close. Please retry after closing them."); }
            Apply(stage, root, Guard);
            instance.ReleaseMutex();
            mutex.ReleaseMutex();
            string launcher = Safe(root, "Rune.exe");
            if (!File.Exists(launcher)) launcher = Safe(root, "Open Rune.exe");
            Process.Start(new ProcessStartInfo(launcher) { UseShellExecute = true, WorkingDirectory = root });
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(stage, "update-error.txt"), error.ToString());
            System.Windows.Forms.MessageBox.Show("Rune could not finish updating.\n\n" + error.Message + "\n\nDetails: " + Path.Combine(stage, "update-error.txt"), "Rune update");
            Environment.ExitCode = 1;
        }
    }
}

