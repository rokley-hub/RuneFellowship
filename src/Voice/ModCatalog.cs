using System.IO.Compression;
using System.Net;
using System.Text.Json;
namespace Rune.Voice;

internal sealed record CatalogMod(string Id, string Name, string Version, string Description, string[] Dependencies, string Download, string Website, bool Deprecated);
internal static class ModCatalog
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(5) };
    internal static async Task<byte[]> Download(string url, int maxBytes, CancellationToken token)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https" || !(uri.Host == "thunderstore.io" || uri.Host.EndsWith(".thunderstore.io", StringComparison.OrdinalIgnoreCase))) throw new IOException("Unexpected download host.");
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) throw new IOException("Download is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(token); using var result = new MemoryStream(); var buffer = new byte[65536]; int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0) { if (result.Length + read > maxBytes) throw new IOException("Download is too large."); await result.WriteAsync(buffer.AsMemory(0, read), token); }
        return result.ToArray();
    }
    internal static async Task DownloadToFile(string url, string destination, CancellationToken token)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https" || !(uri.Host == "thunderstore.io" || uri.Host.EndsWith(".thunderstore.io", StringComparison.OrdinalIgnoreCase))) throw new IOException("Unexpected download host.");
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
        const long limit = 16_000_000_000;
        if (response.Content.Headers.ContentLength > limit) throw new IOException("The package exceeds the 16 GB download limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token); await using var output = File.Create(destination); var buffer = new byte[131072]; int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0) { if (output.Length + count > limit) throw new IOException("Package download is too large."); await output.WriteAsync(buffer.AsMemory(0, count), token); }
    }
    private static byte[] Decode(byte[] data)
    {
        if (data.Length < 2 || data[0] != 31 || data[1] != 139) return data;
        using var input = new MemoryStream(data); using var gzip = new GZipStream(input, CompressionMode.Decompress); using var output = new MemoryStream();
        var buffer = new byte[65536]; int count; while ((count = gzip.Read(buffer)) > 0) { if (output.Length + count > 100_000_000) throw new IOException("Catalog page is too large."); output.Write(buffer, 0, count); } return output.ToArray();
    }
    internal static List<CatalogMod> Cached(string root)
    {
        try { return JsonSerializer.Deserialize<List<CatalogMod>>(File.ReadAllText(Path.Combine(root, "catalog.json"))) ?? new(); } catch { return new(); }
    }
    internal static bool CacheIsFresh(string root, TimeSpan maximumAge)
    {
        string path = Path.Combine(root, "catalog.json");
        return File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) <= maximumAge;
    }
    internal static bool IsNewer(string installed, string available)
    {
        return Version.TryParse(installed, out var current) && Version.TryParse(available, out var latest) && latest > current;
    }
    internal static List<CatalogMod> WithProfileFoundation(IEnumerable<CatalogMod> requested, List<CatalogMod> catalog, IEnumerable<InstalledMod> installed)
    {
        var result = requested.ToList();
        foreach (string id in new[] { "denikson-BepInExPack_Valheim", "ValheimModding-Jotunn" }) {
            if (installed.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
            var foundation = catalog.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (foundation == null) throw new IOException("The required " + id.Split('-').Last() + " package is not in the online catalogue. Refresh online and try again.");
            if (!result.Any(c => c.Id == foundation.Id)) result.Insert(0, foundation);
        }
        return result;
    }
    internal static List<CatalogMod> RuneRequirements(List<CatalogMod> catalog, List<OwnedMod> installed)
    {
        var result = new List<CatalogMod>();
        foreach (var (id, minimum) in new[] { ("denikson-BepInExPack_Valheim", "5.4.2333"), ("ValheimModding-Jotunn", "2.28.0"), ("MathiasDecrock-PlanBuild", "0.18.4") }) {
            var have = installed.FirstOrDefault(m => m.Id == id);
            if (have != null && Version.TryParse(have.Version, out var version) && version >= Version.Parse(minimum)) {
                result.Add(new(have.Id, have.Name, have.Version, "", have.Dependencies, "", "", false));
                continue;
            }
            var package = catalog.FirstOrDefault(m => m.Id == id && !m.Deprecated && Version.TryParse(m.Version, out var available) && available >= Version.Parse(minimum));
            if (package == null) throw new IOException("Could not find the required " + id.Split('-').Last() + " package. Connect to the internet, refresh online and try Install Rune requirements again.");
            result.Add(package);
        }
        return result;
    }
    internal static async Task<List<CatalogMod>> Refresh(string root, IProgress<string> progress, CancellationToken token)
    {
        var urls = JsonSerializer.Deserialize<string[]>(Decode(await Download("https://thunderstore.io/c/valheim/api/v1/package-listing-index/", 2_000_000, token))) ?? Array.Empty<string>();
        if (urls.Length > 100) throw new IOException("Unexpected catalog size.");
        var all = new List<CatalogMod>(); int page = 0;
        foreach (string url in urls) {
            progress.Report($"Loading mod catalog {++page}/{urls.Length}…");
            using var document = JsonDocument.Parse(Decode(await Download(url, 30_000_000, token)));
            foreach (var package in document.RootElement.EnumerateArray()) {
                var versions = package.GetProperty("versions").EnumerateArray().Where(v => !v.TryGetProperty("is_active", out var active) || active.GetBoolean()).ToArray();
                if (versions.Length == 0) continue;
                var latest = versions.OrderByDescending(v => Version.TryParse(v.GetProperty("version_number").GetString(), out var version) ? version : new Version()).First();
                string id = package.GetProperty("full_name").GetString()!;
                if (id == "ebkr-r2modman") continue;
                all.Add(new(id, package.GetProperty("name").GetString()!, latest.GetProperty("version_number").GetString()!, latest.GetProperty("description").GetString() ?? "", latest.GetProperty("dependencies").EnumerateArray().Select(d => d.GetString()!).ToArray(), latest.GetProperty("download_url").GetString()!, package.GetProperty("package_url").GetString()!, package.GetProperty("is_deprecated").GetBoolean()));
            }
        }
        Directory.CreateDirectory(root); string cache = Path.Combine(root, "catalog.json"); File.WriteAllText(cache + ".tmp", JsonSerializer.Serialize(all)); File.Move(cache + ".tmp", cache, true); return all;
    }
    internal static List<CatalogMod> Resolve(IEnumerable<CatalogMod> requested, List<CatalogMod> catalog, List<OwnedMod> installed)
    {
        var ordered = new List<CatalogMod>(); var visiting = new HashSet<string>(); var done = new HashSet<string>();
        void Visit(CatalogMod mod) {
            if (done.Contains(mod.Id)) return;
            if (!visiting.Add(mod.Id)) throw new IOException("Circular dependency: " + mod.Id);
            foreach (string dep in mod.Dependencies) {
                int split = dep.LastIndexOf('-'); if (split <= 0 || !Version.TryParse(dep[(split + 1)..], out var minimum)) throw new IOException("Invalid dependency: " + dep);
                string id = dep[..split];
                var have = installed.FirstOrDefault(m => m.Id == id);
                if (have != null && Version.TryParse(have.Version, out var current) && current >= minimum) {
                    // Visit installed dependency metadata as well: an enabled parent cannot hide disabled grandchildren.
                    Visit(new CatalogMod(have.Id, have.Name, have.Version, "", have.Dependencies, "", "", false)); continue;
                }
                var available = catalog.FirstOrDefault(m => m.Id == id && Version.Parse(m.Version) >= minimum) ?? throw new IOException("Required dependency is unavailable: " + dep);
                Visit(available);
            }
            visiting.Remove(mod.Id); done.Add(mod.Id); ordered.Add(mod);
        }
        foreach (var mod in requested) Visit(mod); return ordered;
    }
}
