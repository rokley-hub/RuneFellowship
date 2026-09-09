using System.Text.Json;
namespace Rune.Voice;
internal static class ModSourceChecks
{
    internal static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var report=new List<string>();
        void Check(bool pass,string name) {if(!pass)throw new Exception(name);report.Add("PASS: "+name);}
        var pages=new Dictionary<string,string>();
        try {
            Check(ModSources.Page("thunderstore","Author-Mod",pages)=="https://thunderstore.io/c/valheim/p/Author/Mod/","Thunderstore package opens its provider page.");
            Check(ModSources.Page("nexus","Author-Mod",pages)==null,"Unknown counterpart never silently switches provider.");
            Check(ModSources.Page("thunderstore","Nexus-1234",pages)==null,"Unknown Nexus import also respects Thunderstore preference.");
            Check(ModSources.Page("nexus","ValheimModding-Jotunn",pages)==NexusMods.Page("Nexus-1138"),"Known dependency links use Nexus when selected.");
            Check(ModSources.Page("thunderstore","Nexus-1138",pages)!.Contains("/ValheimModding/Jotunn/"),"Known dependency links work in reverse.");
            pages["nexus:Author-Mod"]="https://www.nexusmods.com/valheim/mods/1234";
            Check(ModSources.Page("nexus","Author-Mod",pages)==pages["nexus:Author-Mod"],"Saved counterpart opens on preferred site.");
            Check(ModSources.Page("thunderstore","Author-Mod",pages)!.Contains("thunderstore.io"),"Saved Nexus counterpart does not override Thunderstore preference.");
            foreach(var url in new[]{"https://nexusmods.com.evil.test/valheim/mods/1234","http://www.nexusmods.com/valheim/mods/1234","https://www.nexusmods.com:444/valheim/mods/1234","https://www.nexusmods.com/skyrim/mods/1234"})
                Check(!ModSources.ValidPage("nexus",url),"Reject wrong host, protocol, port or game: "+url);
            Check(!ModSources.ValidPage("thunderstore",pages["nexus:Author-Mod"]),"Cannot save other provider's page.");
            string path=Path.Combine(root,"preferences.json");
            File.WriteAllText(path,JsonSerializer.Serialize(new RunePreferences {ModSource="nexus",ModSourcePages=pages,Language="nl",VoiceVolume=37},Brain.Json));
            var saved=ProfileStore.Load(path);
            Check(saved.ModSource=="nexus"&&saved.ModSourcePages.Count==1&&saved.Language=="nl"&&saved.VoiceVolume==37,"Source and counterpart survive preference reload alongside existing settings.");
            File.WriteAllText(path,"{\"modSource\":42,\"language\":\"nl\"}");
            Check(ProfileStore.Load(path).ModSource=="thunderstore"&&ProfileStore.Load(path).Language=="nl","Malformed source does not discard other preferences.");
        }finally{File.WriteAllLines(Path.Combine(root,"report.txt"),report);}
    }
}
