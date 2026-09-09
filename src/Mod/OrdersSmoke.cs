using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Rune.Shared;
using UnityEngine;

namespace Rune.Mod
{
    internal static class OrdersSmoke
    {
        internal static IEnumerator Run(Plugin plugin, Player player, string output)
        {
            var report = new List<string>(); Companion npc = null; Vector3 origin = player.transform.position;
            // Separate harvesting from the explicit combat stage. Random world
            // spawns must not turn this fixture into an unplanned combat test.
            void ClearHarvestThreats() {
                foreach (var creature in Character.GetAllCharacters().ToArray())
                    if (creature && !(creature is Player) && creature.GetComponent<Companion>() == null && Vector3.Distance(creature.transform.position, origin) < 100)
                        UnityEngine.Object.Destroy(creature.gameObject);
            }
            ClearHarvestThreats();
            try {
                var go = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(Plugin.PrefabFor("dwarf")), origin + Vector3.right * 2, Quaternion.identity);
                npc = go.GetComponent<Companion>(); npc.Bind(player, "bot-order-test", "dwarf", "Order test", "female");
            } catch (Exception e) { report.Add("SETUP FAILED: " + e); }
            yield return new WaitForSecondsRealtime(3);
            if (!npc || !npc.Ready) { report.Add("FAILED: dwarf did not initialize"); File.WriteAllLines(output, report); yield break; }
            float previousAttack = 0; Vector3 start = npc.transform.position; bool accepted = false;
            try {
                foreach (var item in npc.Body.GetInventory().GetAllItems().Where(i => i.GetDamage().m_chop > 0).ToArray()) { npc.Body.UnequipItem(item, false); npc.Body.GetInventory().RemoveItem(item); }
                var fists = (ItemDrop.ItemData)AccessTools.Method(typeof(Companion), "GetHarvestTool").Invoke(npc, new object[] { false });
                if (fists == null) throw new Exception("No native unarmed attack available.");
                var canDamage = AccessTools.Method(typeof(Companion), "CanHarvestDamage");
                var sapling = ZNetScene.instance.m_prefabs.FirstOrDefault(p => {
                    var d = p.GetComponent<Destructible>(); var drops = p.GetComponent<DropOnDestroyed>();
                    return d && !p.GetComponent<Piece>() && drops && drops.m_dropWhenDestroyed.m_drops.Any(x => x.m_item && x.m_item.name == "Wood")
                        && d.m_minToolTier <= fists.m_shared.m_toolTier && (bool)canDamage.Invoke(null, new object[] { d, fists });
                });
                if (!sapling) throw new Exception("No punchable wood prefab found.");
                Vector3 treePoint = player.transform.position + Vector3.forward * 8;
                if (Heightmap.GetHeight(treePoint, out float ground)) treePoint.y = ground;
                UnityEngine.Object.Instantiate(sapling, treePoint, Quaternion.identity);
                report.Add("No axe. Native fist damage=" + fists.GetDamage().GetTotalDamage() + "; punchable wood=" + sapling.name);
            } catch (Exception e) { report.Add("FAILED: " + e); }
            yield return new WaitForSecondsRealtime(2);
            try {
                previousAttack = (float)AccessTools.Field(typeof(Companion), "lastAttack").GetValue(npc); start = npc.transform.position;
                var result = plugin.Execute(new Command { id = Guid.NewGuid().ToString(), world = Plugin.World, timestamp = Rules.Now, action = "gather_wood", amount = 20, companionId = npc.Id, appearance = "dwarf", displayName = npc.DisplayName, gender = "female" });
                string response = result.message; accepted = result.accepted;
                report.Add("Without axe: accepted=" + accepted + "; " + response);
                if (!accepted || AccessTools.Field(typeof(Companion), "target").GetValue(npc) == null) throw new Exception("Natural resource search did not find a target.");
            } catch (Exception e) { report.Add("FAILED: " + e); }
            float until = Time.realtimeSinceStartup + 35, moved = 0; bool worked = false; int collected = 0;
            while (accepted && Time.realtimeSinceStartup < until) {
                ClearHarvestThreats();
                yield return new WaitForSecondsRealtime(.5f);
                moved = Mathf.Max(moved, Vector3.Distance(start, npc.transform.position));
                worked = (float)AccessTools.Field(typeof(Companion), "lastAttack").GetValue(npc) > previousAttack;
                collected = (int)AccessTools.Field(typeof(Companion), "gathered").GetValue(npc);
                if (worked || collected > 0) break;
            }
            report.Add("Natural gathering without an axe: moved=" + moved + "; punched=" + worked + "; collected=" + collected + "; status=" + npc.TaskLabel);
            if (accepted && !worked && collected == 0) {
                var current = AccessTools.Field(typeof(Companion), "target").GetValue(npc) as ResourceTarget;
                report.Add("FAILED: no harvesting or pickup after a naturally selected target. Target=" + (current?.Component ? current.Component.name : "none") + "; position=" + npc.transform.position + "; approach=" + (current == null ? "none" : current.Approach(npc.transform.position).ToString()));
            }
            try {
                string rejectedId = Guid.NewGuid().ToString();
                var rejected = plugin.Execute(new Command { id = rejectedId, world = Plugin.World, timestamp = Rules.Now, action = "gather_item", item = "nonexistent smoke resource", amount = 5, companionId = npc.Id, appearance = "dwarf", displayName = npc.DisplayName, gender = "female" });
                string response = rejected.message;
                if (npc.LastOrderAccepted || !npc.TaskLabel.StartsWith("Blocked:") || !response.Contains("No gathering order started")) throw new Exception("Unavailable resource did not report failure.");
                if (rejected.accepted || npc.DiagnosticCommand != rejectedId || !npc.DiagnosticRejected) throw new Exception("Rejected order retained the old job identity.");
                npc.Tick(.1f);
                if (!npc.TaskLabel.Contains("Blocked:") || npc.DiagnosticState().outcome != "rejected" || npc.DiagnosticState().commandId != rejectedId) throw new Exception("Rejected state lost its block reason, outcome or command identity after a game tick.");
                report.Add("Unavailable resource: reason returned, new rejected command identity, no silent return to follow.");
            } catch (Exception e) { report.Add("FAILED: " + e); }
            try {
                var order = plugin.Execute(new Command { id = Guid.NewGuid().ToString(), world = Plugin.World, timestamp = Rules.Now, action = "defend", companionId = npc.Id, appearance = "dwarf", displayName = npc.DisplayName, gender = "female" });
                if (!order.accepted || npc.DiagnosticRejected || npc.TaskLabel.StartsWith("Blocked:")) throw new Exception("Defense did not replace the blocked task: " + order.message);
                report.Add("Defense command accepted: " + order.message);
                var point = npc.transform.position + Vector3.right * 5;
                if (Heightmap.GetHeight(point, out float ground)) point.y = ground;
                UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Greydwarf"), point, Quaternion.identity);
            } catch (Exception e) { report.Add("FAILED: " + e); }
            float fightUntil = Time.realtimeSinceStartup + 15; bool acquired = false;
            while (Time.realtimeSinceStartup < fightUntil) {
                yield return new WaitForSecondsRealtime(.3f);
                var enemy = npc.GetComponent<MonsterAI>().GetTargetCreature();
                if (enemy && BaseAI.IsEnemy(npc.Body, enemy)) { acquired = true; break; }
            }
            report.Add(acquired ? "Defense acquired a nearby hostile creature through native combat AI." : "FAILED: Defense did not acquire a nearby hostile creature.");
            File.WriteAllLines(output, report);
        }
    }
}
