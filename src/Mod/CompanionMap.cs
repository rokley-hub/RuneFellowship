using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Plugin
    {
        private readonly Dictionary<Companion, Minimap.PinData> companionPins = new Dictionary<Companion, Minimap.PinData>();
        private BepInEx.Configuration.ConfigEntry<bool> trackCompanionTeam;
        private BepInEx.Configuration.ConfigEntry<string> trackedCompanionIds;
        private bool TeamTrackingEnabled => Rune.Shared.Rules.Now - overlay.timestamp < 5 ? overlay.trackTeam : trackCompanionTeam == null || trackCompanionTeam.Value;
        private bool IsCompanionTracked(string id) => TeamTrackingEnabled && (Rune.Shared.Rules.Now-overlay.timestamp<5 && overlay.trackedCompanions!=null ? overlay.trackedCompanions.Contains(id) : trackedCompanionIds==null || trackedCompanionIds.Value=="*" || trackedCompanionIds.Value.Split(',').Contains(id));
        private readonly Dictionary<string,Minimap.PinData> distantPins = new Dictionary<string,Minimap.PinData>();
        private float distantUpdateAt;
        private static string CompanionLocation(Companion c) {
            Vector3 delta=c.transform.position-Player.m_localPlayer.transform.position;
            float distance=new Vector2(delta.x,delta.z).magnitude;
            string[] directions={"N","NE","E","SE","S","SW","W","NW"};
            int direction=Mathf.RoundToInt(Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg/45f+8)%8;
            return distance<3 ? "Beside you" : distance.ToString("F0")+" m · "+directions[direction];
        }
        private Minimap pinMap;
        private float pinUpdateAt;
        private void LateUpdate()
        {
            if (Time.unscaledTime < pinUpdateAt) return;
            pinUpdateAt = Time.unscaledTime + .3f;
            if(trackedCompanionIds!=null && Rune.Shared.Rules.Now-overlay.timestamp<5 && overlay.trackedCompanions!=null) {
                string ids=string.Join(",",overlay.trackedCompanions);
                if(trackedCompanionIds.Value!=ids)trackedCompanionIds.Value=ids;
            }
            if (pinMap != Minimap.instance) { RemoveCompanionPins(); pinMap = Minimap.instance; }
            if (!pinMap) return;
            if (!TeamTrackingEnabled) { RemoveCompanionPins(); return; }
            foreach(var id in distantPins.Keys.ToArray()) if(!IsCompanionTracked(id)) {pinMap.RemovePin(distantPins[id]);distantPins.Remove(id);}
            var visible = Player.m_localPlayer && Solo ? Companion.Instances.Where(c => c && c.Ready && IsCompanionTracked(c.Id) && !c.Body.IsDead() && c.Owner == Player.m_localPlayer.GetPlayerID()).ToArray() : new Companion[0];
            foreach (var old in companionPins.Keys.ToArray()) if (!visible.Contains(old)) { pinMap.RemovePin(companionPins[old]); companionPins.Remove(old); }
            UpdateDistantPins(visible);
            foreach (var c in visible) {
                if (!companionPins.TryGetValue(c, out var pin)) { pin = pinMap.AddPin(c.transform.position, Minimap.PinType.Player, c.DisplayName, false, false, 0, default); companionPins[c] = pin; }
                pin.m_pos = c.transform.position; pin.m_name = c.DisplayName; pin.m_save = false;
            }
        }
        private void UpdateDistantPins(Companion[] visible) {
            if (Time.unscaledTime < distantUpdateAt) return;
            distantUpdateAt=Time.unscaledTime+5;
            var found=new HashSet<string>();
            if(Player.m_localPlayer && Solo && ZDOMan.instance != null) {
                var zdos=new List<ZDO>();
                foreach(string appearance in new[]{"skeleton","draugr","elite","dwarf","wolf"}) {
                    int index=0; while(!ZDOMan.instance.GetAllZDOsWithPrefabIterative(PrefabFor(appearance),zdos,ref index)) { }
                }
                foreach(var zdo in zdos) {
                    string id=zdo.GetString("rune.id", "");
                    if(!IsCompanionTracked(id) || id.Length==0 || zdo.GetLong("rune.owner",0)!=Player.m_localPlayer.GetPlayerID() || zdo.GetFloat("health",1)<=0 || visible.Any(c=>c.Id==id))continue;
                    found.Add(id);
                    if(!distantPins.TryGetValue(id,out var pin)) {pin=pinMap.AddPin(zdo.GetPosition(),Minimap.PinType.Player,"",false,false,0,default);distantPins[id]=pin;}
                    pin.m_pos=zdo.GetPosition();pin.m_name=zdo.GetString("rune.name",id)+" · last known";pin.m_save=false;
                }
            }
            foreach(var old in distantPins.Keys.ToArray()) if(!found.Contains(old)) {pinMap.RemovePin(distantPins[old]);distantPins.Remove(old);}
        }
        private void RemoveCompanionPins() { if (pinMap) foreach (var pin in companionPins.Values) pinMap.RemovePin(pin); companionPins.Clear(); if(pinMap) foreach(var pin in distantPins.Values) pinMap.RemovePin(pin); distantPins.Clear(); distantUpdateAt=0; }
    }
}
