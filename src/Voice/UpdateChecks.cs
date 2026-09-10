using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rune.Voice;

internal static class UpdateChecks
{
    internal static void Run(string output)
    {
        var checks = new List<string>();
        string fixture = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "update-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add("PASS " + name); }
        void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (IOException) { rejected = true; } Check(rejected, name); }
        string Put(string root, string p, string text) { string file = RuneUpdates.Safe(root,p); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file,text); return file; }
        string Snapshot(string root) => string.Join("\n", Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(p => !p.Contains("release-backups")).Order().Select(p => Path.GetRelativePath(root,p)+":"+RuneUpdates.Hash(p)));
        try
        {
            string stage = Path.Combine(fixture,"stage"), root = Path.Combine(fixture,"installed with spaces");
            var files = new Dictionary<string,string> { ["app/RuneVoice.dll"]="new app", ["app/RuneVoice.runtimeconfig.json"]="{}", ["payload/BepInEx/plugins/RuneCompanion/RuneCompanion.dll"]="new plugin", ["release.json"]="{\"version\":\"0.4.27-beta\",\"desktop\":\"0.4.27\",\"gameplay\":\"0.3.16\"}" };
            foreach(var f in files) Put(stage,f.Key,f.Value);
            var manifest = new UpdateManifest(1,"0.4.27","0.3.16",files.Select(f => new UpdateFile(f.Key,new FileInfo(RuneUpdates.Safe(stage,f.Key)).Length,RuneUpdates.Hash(RuneUpdates.Safe(stage,f.Key)))).ToArray());
            Put(stage,"update.json",JsonSerializer.Serialize(manifest));
            Put(root,"release.json","{\"version\":\"0.4.26-beta\"}"); Put(root,"app/RuneVoice.dll","old app"); Put(root,"payload/BepInEx/plugins/RuneCompanion/RuneCompanion.dll","old plugin");
            Put(root,"bridge/preferences.json","private preferences"); Put(root,"bridge/memories/rune.json","private memory"); Put(root,"runtime/model.bin","model remains");
            Put(root,"files.json","{\"format\":1,\"release\":\"0.4.26-beta\",\"files\":[{\"path\":\"runtime/model.bin\",\"bytes\":13,\"sha256\":\"unchanged\"}]}");
            foreach(bool enabled in new[]{true,false})
            {
                string profile = RuneUpdates.Safe(root,"mod-profiles/"+(enabled?"enabled":"disabled"));
                string relative = "BepInEx/plugins/RuneCompanion/RuneCompanion.dll";
                Put(profile,relative+(enabled?"":".old"),"old profile plugin");
                Put(profile,"BepInEx/config/local.rune.companion.cfg","custom settings"); Put(profile,"BepInEx/plugins/OtherMod/Other.dll","other author");
                OwnedMods.Save(profile,new(){new("RuneCompanion","Rune Fellowship","0.3.15",enabled,Array.Empty<string>(),new[]{relative})});
            }
            string before=Snapshot(root);
            Reject(()=>RuneUpdates.Apply(stage,root,()=>throw new IOException("Game running")),"running game blocks all changes"); Check(Snapshot(root)==before,"blocked update leaves installation unchanged");
            Reject(()=>RuneUpdates.Apply(stage,root,afterWrite:n=>{if(n==6)throw new IOException("Simulated locked file");}),"mid-install failure reported"); Check(Snapshot(root)==before,"failure restores app, plugin, profile manifests and settings");
            Put(stage,"app/RuneVoice.dll","tampered"); Reject(()=>RuneUpdates.Apply(stage,root),"corrupt package rejected before install"); Check(Snapshot(root)==before,"corruption leaves installation unchanged"); Put(stage,"app/RuneVoice.dll","new app");
            foreach(string path in new[]{"../escape","app/../../escape","app/CON.txt","bridge/preferences.json"})
                if(path.StartsWith("bridge")) Check(!RuneUpdates.Allowed(path),"user data excluded from update payload"); else Reject(()=>RuneUpdates.Safe(root,path),"reject unsafe path "+path);
            string badZip=Path.Combine(fixture,"bad.zip"); using(var zip=ZipFile.Open(badZip,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("app/../../escape").Open());writer.Write("bad");}
            Reject(()=>RuneUpdates.Extract(badZip,Path.Combine(fixture,"bad-stage")),"archive traversal blocked");
            RuneUpdates.Apply(stage,root);
            Check(File.ReadAllText(RuneUpdates.Safe(root,"app/RuneVoice.dll"))=="new app","new app installed");
            foreach(bool enabled in new[]{true,false})
            {
                string profile=RuneUpdates.Safe(root,"mod-profiles/"+(enabled?"enabled":"disabled")); var mod=OwnedMods.Read(profile).Single();
                Check(mod.Enabled==enabled && mod.Version=="0.3.16" && File.ReadAllText(RuneUpdates.Safe(profile,mod.Files[0]+(enabled?"":".old")))=="new plugin","profile plugin updated with enabled state preserved: "+enabled);
                Check(File.ReadAllText(RuneUpdates.Safe(profile,"BepInEx/config/local.rune.companion.cfg"))=="custom settings" && File.ReadAllText(RuneUpdates.Safe(profile,"BepInEx/plugins/OtherMod/Other.dll"))=="other author","profile configs and other mods preserved: "+enabled);
            }
            Check(File.ReadAllText(RuneUpdates.Safe(root,"bridge/preferences.json"))=="private preferences" && File.ReadAllText(RuneUpdates.Safe(root,"bridge/memories/rune.json"))=="private memory" && File.ReadAllText(RuneUpdates.Safe(root,"runtime/model.bin"))=="model remains","preferences, memories and models preserved");
            Check(JsonNode.Parse(File.ReadAllText(RuneUpdates.Safe(root,"files.json")))!["files"]!.AsArray().Any(n=>n!["path"]!.GetValue<string>()=="runtime/model.bin"),"runtime ownership receipt retained");
            Reject(()=>RuneUpdates.Apply(stage,root),"same version and downgrades blocked");
            object Release(string tag,bool beta,string prefix) => new{draft=false,prerelease=beta,tag_name=tag,assets=new[]{new{name="Rune-Update-"+tag[1..]+".zip",browser_download_url=prefix+"Rune-Update-"+tag[1..]+".zip"},new{name="Rune-Update-"+tag[1..]+".zip.sha256",browser_download_url=prefix+"Rune-Update-"+tag[1..]+".zip.sha256"}}};
            string tag="v0.4.28-beta", prefix=RuneUpdates.Repository+"/releases/download/"+tag+"/";
            var feed=JsonSerializer.Serialize(new[]{Release(tag,true,prefix)});
            Check(RuneUpdates.Select(feed,"0.4.27-beta")?.Version=="0.4.28-beta","beta installations discover newer beta releases");
            Check(RuneUpdates.Select(feed,"0.4.27")==null,"stable installations do not switch to beta");
            Check(RuneUpdates.Select(JsonSerializer.Serialize(new[]{Release(tag,true,"https://example.com/")}),"0.4.27-beta")==null,"untrusted download destination rejected");
            Check(RuneUpdates.Select("[]","0.4.27-beta")==null,"missing release assets handled");
            var worker=RuneUpdates.Worker(stage,root); Check(worker.ArgumentList.Contains(root) && worker.CreateNoWindow,"worker preserves paths with spaces and stays hidden");
        }
        catch(Exception error){checks.Add("FAIL "+error);Environment.ExitCode=1;}
        File.WriteAllLines(output,checks);
    }
}
