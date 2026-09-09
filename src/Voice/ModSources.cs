using System.Text.RegularExpressions;
namespace Rune.Voice;

internal static class ModSources
{
    // Verified publisher pages. These map links, never imply identical package versions.
    private static readonly Dictionary<string,string> NexusPages = new() {
        ["ValheimModding-Jotunn"] = "1138",
        ["denikson-BepInExPack_Valheim"] = "3605",
        ["ValheimModding-HookGenPatcher"] = "505"
    };
    internal static string Normalize(string? source) => source == "nexus" ? "nexus" : "thunderstore";
    internal static string Label(string source) => Normalize(source) == "nexus" ? "Nexus Mods" : "Thunderstore";
    internal static string Browse(string source) => Normalize(source) == "nexus" ? NexusMods.BrowseUrl : "https://thunderstore.io/c/valheim/";
    internal static bool ValidPage(string source, string url)
    {
        if (!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.UserInfo.Length!=0 || !uri.IsDefaultPort) return false;
        if(source=="nexus") {try {NexusMods.IdFromUrl(url);return true;}catch(IOException){return false;}}
        return source=="thunderstore" && uri.Host=="thunderstore.io" && Regex.IsMatch(uri.AbsolutePath,@"^/c/valheim/p/[A-Za-z0-9_]+/[A-Za-z0-9_]+/?$");
    }
    internal static string? Page(string source, string id, IReadOnlyDictionary<string,string> saved)
    {
        source=Normalize(source);
        if(saved.TryGetValue(source+":"+id,out var page)&&ValidPage(source,page))return page;
        if(source=="nexus")return NexusMods.IsNexus(id)?NexusMods.Page(id):NexusPages.TryGetValue(id,out var nexusId)?NexusMods.Page("Nexus-"+nexusId):null;
        if(NexusMods.IsNexus(id)) {
            string? counterpart=NexusPages.FirstOrDefault(p=>"Nexus-"+p.Value==id).Key;
            if(counterpart==null)return null;
            id=counterpart;
        }
        if(NexusMods.IsNexus(id)||!Regex.IsMatch(id,@"^[A-Za-z0-9_]+-[A-Za-z0-9_]+$"))return null;
        var parts=id.Split('-',2);return "https://thunderstore.io/c/valheim/p/"+parts[0]+"/"+parts[1]+"/";
    }
}
