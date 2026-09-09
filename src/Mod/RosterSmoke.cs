using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Rune.Shared;
using UnityEngine;
namespace Rune.Mod
{
    internal static class RosterSmoke
    {
        internal static IEnumerator Run(Plugin plugin,Player player,string output)
        {
            var report=new List<string>();
            var ids=new List<string>();
            foreach(string appearance in new[]{"dwarf","dwarf","skeleton","draugr","elite","wolf"}) {
                string id="fixture"+ids.Count,gender=ids.Count==1?"female":"male";
                try {
                    var reply=plugin.Execute(new Command {id=Guid.NewGuid().ToString(),timestamp=Rules.Now,world=Plugin.World,action="summon",companionId=id,displayName="Test "+appearance+" "+ids.Count,appearance=appearance,gender=gender});
                    if(!reply.accepted)throw new Exception(reply.message);
                    ids.Add(id);
                }catch(Exception ex){report.Add("FAIL summon "+appearance+": "+ex.Message);}
                yield return new WaitForSecondsRealtime(2);
                try {
                    var npc=Plugin.Find(id);
                    if(!npc || !npc.Ready || npc.Body.GetHealth()<=0)throw new Exception("Companion not alive and ready");
                    npc.transform.position=player.transform.position+Vector3.right*(ids.Count*3);
                    npc.Order("stay",1);
                    report.Add("PASS summoned "+appearance+" / "+gender+" with independent ID "+id);
                    if(appearance=="dwarf")report.Add((npc.GetComponent<VisEquipment>().GetModelIndex()==(gender=="female"?1:0)?"PASS":"FAIL")+" dwarf visual model "+gender);
                }catch(Exception ex){report.Add("FAIL readiness "+id+": "+ex.Message);}
            }
            yield return new WaitForSecondsRealtime(1);
            try {
                report.Add((ids.Count==6&&ids.All(id=>Plugin.Find(id))?"PASS":"FAIL")+" six companions coexist");
                var first=Plugin.Find(ids[0]);
                var reply=plugin.Execute(new Command{id=Guid.NewGuid().ToString(),timestamp=Rules.Now,world=Plugin.World,action="follow",companionId=ids[0]});
                if(!reply.accepted)throw new Exception(reply.message);
                report.Add((ids.Skip(1).All(id=>Plugin.Find(id).TaskLabel == "Holding position")?"PASS":"FAIL")+" follow only changes selected companion; others: "+string.Join(", ",ids.Skip(1).Select(id=>Plugin.Find(id).TaskLabel)));
                var pins=(System.Collections.IDictionary)HarmonyLib.AccessTools.Field(typeof(Plugin),"companionPins").GetValue(plugin);
                report.Add((ids.All(id=>pins.Contains(Plugin.Find(id)))?"PASS":"FAIL")+" each live companion has a map pin");
                HarmonyLib.AccessTools.Field(typeof(Plugin),"pinUpdateAt").SetValue(plugin,0f);
                HarmonyLib.AccessTools.Method(typeof(Plugin),"LateUpdate").Invoke(plugin,null);
                report.Add((ids.All(id=>Vector3.Distance(((Minimap.PinData)pins[Plugin.Find(id)]).m_pos,Plugin.Find(id).transform.position)<2)?"PASS":"FAIL")+" map markers follow companion positions");
                var bench=UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("piece_workbench"),player.transform.position+Vector3.forward*8,Quaternion.identity).GetComponent<Piece>();
                bench.AssignCreator(player.GetPlayerID());
                report.Add((bench.GetCreator()==player.GetPlayerID()?"PASS":"FAIL")+" building ownership uses current game creator API");
                var far=ZDOMan.instance.CreateNewZDO(player.transform.position+Vector3.right*1000,Plugin.PrefabFor("wolf").GetStableHashCode());
                far.SetPrefab(Plugin.PrefabFor("wolf").GetStableHashCode());
                far.Set("rune.owner",player.GetPlayerID()); far.Set("rune.id","fixture-far");far.Set("rune.name","Distant test");
                HarmonyLib.AccessTools.Field(typeof(Plugin),"distantUpdateAt").SetValue(plugin,0f);
                HarmonyLib.AccessTools.Method(typeof(Plugin),"UpdateDistantPins").Invoke(plugin,new object[]{ids.Select(id=>Plugin.Find(id)).ToArray()});
                var distant=(System.Collections.IDictionary)HarmonyLib.AccessTools.Field(typeof(Plugin),"distantPins").GetValue(plugin);
                report.Add((distant.Contains("fixture-far")&&((Minimap.PinData)distant["fixture-far"]).m_name.Contains("last known")?"PASS":"FAIL")+" unloaded owned companion gets last-known marker");
                var tracking=(BepInEx.Configuration.ConfigEntry<bool>)HarmonyLib.AccessTools.Field(typeof(Plugin),"trackCompanionTeam").GetValue(plugin);
                tracking.Value=false;HarmonyLib.AccessTools.Field(typeof(Plugin),"pinUpdateAt").SetValue(plugin,0f);
                HarmonyLib.AccessTools.Method(typeof(Plugin),"LateUpdate").Invoke(plugin,null);
                report.Add((pins.Count==0&&distant.Count==0?"PASS":"FAIL")+" tracking off removes all companion markers");
                tracking.Value=true;
                var overlay=HarmonyLib.AccessTools.Field(typeof(Plugin),"overlay").GetValue(plugin);
                HarmonyLib.AccessTools.Field(overlay.GetType(),"timestamp").SetValue(overlay,Rules.Now);
                HarmonyLib.AccessTools.Field(overlay.GetType(),"trackedCompanions").SetValue(overlay,new[]{ids[0]});
                HarmonyLib.AccessTools.Field(typeof(Plugin),"pinUpdateAt").SetValue(plugin,0f);
                HarmonyLib.AccessTools.Method(typeof(Plugin),"LateUpdate").Invoke(plugin,null);
                report.Add((pins.Count==1 && pins.Contains(Plugin.Find(ids[0])) && distant.Count==0?"PASS":"FAIL")+" per-companion tracking hides other team members");


            }catch(Exception ex){report.Add("FAIL roster: "+ex);}
            try {
                HarmonyLib.AccessTools.Field(typeof(Plugin),"controlCompanion").SetValue(plugin,ids[0]);
                HarmonyLib.AccessTools.Method(typeof(Plugin),"LocalOrder").Invoke(plugin,new object[]{"dismiss",20,""});
            }catch(Exception ex){report.Add("FAIL unsummon control: "+ex);}
            yield return new WaitForSeconds(.5f);
            report.Add((!Plugin.Find(ids[0]) && ids.Skip(1).All(id=>Plugin.Find(id))?"PASS":"FAIL")+" in-game Unsummon removes only the selected companion");
            System.IO.File.WriteAllLines(output,report);
            Application.Quit();
        }
    }
}
