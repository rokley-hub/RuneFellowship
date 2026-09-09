using System.IO.Compression;
namespace Rune.Voice;
internal static class NexusModChecks
{
    internal static void Run(string root)
    {
        root=Path.GetFullPath(root);Directory.CreateDirectory(root);
        var report=new List<string>();
        void Check(bool pass,string message) {if(!pass)throw new Exception(message);report.Add("PASS: "+message);}
        string Zip(string name,params (string Path,string Text)[] files) {
            string path=Path.Combine(root,name+".zip");using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
            foreach(var f in files){using var w=new StreamWriter(zip.CreateEntry(f.Path).Open());w.Write(f.Text);}return path;
        }
        try {
            string profile=OwnedMods.Create(root,"Nexus fixture");
            const string page="https://www.nexusmods.com/valheim/mods/1234";
            Check(NexusMods.IdFromUrl(page+"?tab=files")=="Nexus-1234"&&NexusMods.Page("Nexus-1234")==page,"Canonical source identity survives query parameters.");
            foreach(string bad in new[]{"https://nexusmods.com.evil.test/valheim/mods/1234","https://www.nexusmods.com/skyrim/mods/1234","https://www.nexusmods.com/valheim/mods/../1234","http://www.nexusmods.com/valheim/mods/1234"}) {
                bool blocked=false;try{NexusMods.IdFromUrl(bad);}catch(IOException){blocked=true;}Check(blocked,"Rejects wrong host, game or address: "+bad);
            }
            string first=Zip("first",("Wrapper/BepInEx/plugins/Test/Test.dll","version one"),("Wrapper/BepInEx/plugins/Test/art.bundle","asset"),("Wrapper/BepInEx/config/test.cfg","default"),("Wrapper/LICENSE.md","author license"));
            OwnedMods.InstallNexus(profile,first,page,"Example","1.0");
            Check(File.ReadAllText(Path.Combine(profile,"BepInEx/plugins/Nexus-1234/LICENSE.md"))=="author license","Nexus import retains author license documents.");
            string plugin=Path.Combine(profile,"BepInEx/plugins/Nexus-1234/Test/Test.dll"),config=Path.Combine(profile,"BepInEx/config/test.cfg");
            Check(File.ReadAllText(plugin)=="version one"&&File.Exists(config),"Wrapper BepInEx layout maps plugin assets and shared config correctly.");
            OwnedMods.SaveConfig(profile,"BepInEx/config/test.cfg","user value");OwnedMods.Toggle(profile,"Nexus-1234");
            string second=Zip("second",("plugins/Test/Test.dll","version two"),("config/test.cfg","new default"));
            OwnedMods.InstallNexus(profile,second,page,"Example","2.0");
            Check(File.ReadAllText(plugin+".old")=="version two"&&!File.Exists(plugin)&&File.ReadAllText(config)=="user value","Update keeps disabled state and user settings.");
            Check(!File.Exists(Path.Combine(profile,"BepInEx/plugins/Nexus-1234/Test/art.bundle.old")),"Update removes obsolete owned assets.");
            OwnedMods.Toggle(profile,"Nexus-1234");
            int i=0;
            foreach(var bad in new[]{new[]{("../escaped.dll","bad")},new[]{("Test.dll","duplicate"),("test.dll","duplicate")},new[]{("fomod/ModuleConfig.xml","installer"),("Test.dll","x")},new[]{("setup.exe","x"),("Test.dll","x")},new[]{("bad.zip","x"),("Test.dll","x")},new[]{("CON.dll","x")}}) {
                bool blocked=false;try{OwnedMods.InstallNexus(profile,Zip("bad"+(i++),bad),page,"Example","3.0");}catch(IOException){blocked=true;}
                Check(blocked&&File.ReadAllText(plugin)=="version two","Invalid package is rejected without changing current installation "+i);
            }
            bool duplicate=false;try{OwnedMods.InstallNexus(profile,Zip("duplicate",("Test.dll","copy")),"https://www.nexusmods.com/valheim/mods/5555","Duplicate","1");}catch(IOException){duplicate=true;}
            Check(duplicate&&OwnedMods.Read(profile).Count==1,"Duplicate plugin from another package cannot load twice.");
            string xml=Path.Combine(root,"profile.xml");ModProfileXml.Export(profile,xml);
            Check(File.ReadAllText(xml).Contains("Nexus-1234"),"XML backup retains Nexus source identity.");
            OwnedMods.Remove(profile,"Nexus-1234");
            Check(!File.Exists(plugin)&&File.ReadAllText(config)=="user value","Removing Nexus mod retains user config.");
            OwnedMods.RestoreLast(profile);
            Check(File.ReadAllText(plugin)=="version two"&&OwnedMods.Read(profile).Single().Version=="2.0","Profile backup restores Nexus mod.");
        }finally{File.WriteAllLines(Path.Combine(root,"report.txt"),report);}
    }
}
