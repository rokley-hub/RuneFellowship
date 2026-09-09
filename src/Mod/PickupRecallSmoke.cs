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
    internal static class PickupRecallSmoke
    {
        internal static IEnumerator Run(Plugin plugin, Player player, string output)
        {
            var report = new List<string>(); Companion npc = null;
            try {
                var go = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(Plugin.PrefabFor("dwarf")), player.transform.position + Vector3.right * 2, Quaternion.identity);
                npc = go.GetComponent<Companion>(); npc.Bind(player, "bot-pickup-test", "dwarf", "Pickup test", "female");
            } catch (Exception e) { report.Add("FAILED setup: " + e); }
            yield return new WaitForSecondsRealtime(3);
            if (!npc || !npc.Ready) { File.WriteAllText(output, "FAILED: companion did not initialize."); yield break; }
            int initialStone = player.GetInventory().CountItems("$item_stone"); bool accepted = false;
            try {
                var stone = ZNetScene.instance.GetPrefab("Pickable_Stone");
                if (!stone || !stone.GetComponent<Pickable>()) throw new Exception("Native Pickable_Stone prefab missing.");
                foreach (var item in npc.Body.GetInventory().GetAllItems().Where(i => i.GetDamage().m_pickaxe > 0).ToArray()) npc.Body.GetInventory().RemoveItem(item);
                for (int i = 0; i < 2; i++) {
                    var point = player.transform.position + Vector3.forward * (4 + i * 2);
                    if (Heightmap.GetHeight(point, out float ground)) point.y = ground;
                    UnityEngine.Object.Instantiate(stone, point, Quaternion.identity);
                }
                report.Add("Native stone source=" + stone.name + "; companion has no pickaxe.");
            } catch (Exception e) { report.Add("FAILED fixture: " + e); }
            yield return new WaitForSecondsRealtime(2);
            try {
                var reply = plugin.Execute(new Command { id = Guid.NewGuid().ToString(), world = Plugin.World, timestamp = Rules.Now, action = "gather_stone", amount = 2, companionId = npc.Id, appearance = "dwarf", displayName = npc.DisplayName, gender = "female" });
                accepted = reply.accepted; report.Add("Gather stone accepted=" + accepted + ": " + reply.message);
                if (!accepted) throw new Exception("Gather rejected native stone pickables.");
            } catch (Exception e) { report.Add("FAILED: " + e); }
            float until = Time.realtimeSinceStartup + 60, logAt = 0;
            while (accepted && player.GetInventory().CountItems("$item_stone") < initialStone + 2 && Time.realtimeSinceStartup < until) {
                if (Time.realtimeSinceStartup >= logAt) {
                    logAt = Time.realtimeSinceStartup + 5;
                    var current = (ResourceTarget)AccessTools.Field(typeof(Companion), "target").GetValue(npc);
                    report.Add("Progress: " + npc.TaskLabel + "; cargoStone=" + npc.Count("Stone") + "; player=" + player.transform.position + "; npc=" + npc.transform.position + "; enemies=" + npc.ThreatCount() + "; target=" + (current == null ? "none" : current.Component.name + " at " + current.Approach(npc.transform.position)));
                    File.WriteAllLines(output, report);
                }
                yield return new WaitForSecondsRealtime(.3f);
            }
            int delivered = player.GetInventory().CountItems("$item_stone") - initialStone;
            report.Add((delivered >= 2 ? "PASS" : "FAILED") + ": gathered and delivered " + delivered + " stone through gather_stone, without a pickaxe.");
            try {
                npc.Order("pickup_all", 100);
                string reply = npc.Order("follow", 20);
                var type = typeof(Companion);
                if ((bool)AccessTools.Field(type, "autoPickup").GetValue(npc) || AccessTools.Field(type, "target").GetValue(npc) != null || AccessTools.Field(type, "craftPlan").GetValue(npc) != null || (string)AccessTools.Field(type, "mode").GetValue(npc) != "follow") throw new Exception("Follow left a work action active.");
                npc.Tick(.1f);
                if (npc.GetComponent<MonsterAI>().GetTargetCreature()) throw new Exception("Follow retained a combat target.");
                report.Add("PASS: Follow cancelled automatic pickup and cleared work/combat targets. " + reply);
                npc.QueuePlan(new[] { new PlanStep { action = "gather_wood", amount = 4 }, new PlanStep { action = "return" } });
                npc.Order("follow", 20); npc.Tick(.1f);
                var pending = (System.Collections.ICollection)AccessTools.Field(type, "pendingSteps").GetValue(npc);
                if (pending.Count != 0 || AccessTools.Field(type, "activeStep").GetValue(npc) != null) throw new Exception("Follow retained plan steps.");
                report.Add("PASS: Follow cleared pending and active plan steps.");
                var wrap = (GUIStyle)AccessTools.Field(typeof(Plugin), "overlayWrap").GetValue(plugin);
                if (wrap == null) report.Add("NOT VISUALLY TESTED: hidden game did not initialize the overlay GUI.");
                else {
                    if (!wrap.wordWrap || wrap.fontSize < 13) throw new Exception("Overlay status style is not enlarged and wrapped.");
                    float textHeight = wrap.CalcHeight(new GUIContent("Blocked: The remaining flints are on unsafe terrain. Move closer to a safe source and ask again."), 430);
                    report.Add("PASS: Overlay status wraps at 430px content width; measured height=" + textHeight + ".");
                }
            } catch (Exception e) { report.Add("FAILED: " + e); }
            // Replay the run's equipment request with a real dropped axe and normal movement.
            try {
                npc.Order("stay", 20);
                foreach (var item in npc.Body.GetInventory().GetAllItems().Where(i => i.GetDamage().m_chop > 0).ToArray()) { npc.Body.UnequipItem(item, false); npc.Body.GetInventory().RemoveItem(item); }
                var point = npc.transform.position + Vector3.forward * 2;
                if (Heightmap.GetHeight(point, out float ground)) point.y = ground + .3f;
                UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("AxeStone"), point, Quaternion.identity);
            } catch (Exception e) { report.Add("FAILED equipment fixture: " + e); }
            yield return new WaitForSecondsRealtime(3);
            try {
                var command = Rules.Parse("pick up the stone axe and use it.");
                command.id = Guid.NewGuid().ToString(); command.world = Plugin.World; command.timestamp = Rules.Now; command.companionId = npc.Id; command.appearance = "dwarf"; command.displayName = npc.DisplayName;
                var reply = plugin.Execute(command); accepted = reply.accepted;
                report.Add("Pickup/equip accepted=" + accepted + ": " + reply.message);
                if (!accepted) throw new Exception(reply.message);
            } catch (Exception e) { report.Add("FAILED equipment order: " + e); }
            until = Time.realtimeSinceStartup + 35;
            while (accepted && !npc.Body.GetInventory().GetAllItems().Any(i => i.m_customData.ContainsKey("rune.personal")) && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(.3f);
            try {
                var axe = npc.Body.GetInventory().GetAllItems().Single(i => i.m_dropPrefab && i.m_dropPrefab.name == "AxeStone");
                if (!axe.m_customData.ContainsKey("rune.personal") || !axe.m_equipped || npc.CargoCount != 0) throw new Exception("Axe not equipped and retained as personal equipment.");
                report.Add("PASS: Real dropped stone axe picked up, equipped and retained, without searching for twenty axes. " + npc.ToolStatus);
                var replacement = npc.SnapshotForReplacement(); var restored = new Inventory("equipment-test", null, 8, 4); restored.Load(new ZPackage(Convert.FromBase64String(replacement.Inventory)));
                if (!restored.GetAllItems().Any(i => i.m_customData.ContainsKey("rune.personal"))) throw new Exception("Personal equipment lost during save/body replacement.");
                report.Add("PASS: Personal equipment survives native inventory serialization and body replacement snapshot.");
                npc.Body.GetInventory().AddItem(ZNetScene.instance.GetPrefab("Stone"), 3);
                npc.transform.position = player.transform.position + Vector3.right * 2;
                npc.Order("return", 20);
            } catch (Exception e) { report.Add("FAILED equipment: " + e); }
            until = Time.realtimeSinceStartup + 10;
            while (npc.CargoCount > 0 && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(.3f);
            Companion replacementNpc = null;
            try {
                var snapshot = npc.SnapshotForReplacement();
                var go = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(Plugin.PrefabFor("dwarf")), player.transform.position + Vector3.left * 2, Quaternion.identity);
                replacementNpc = go.GetComponent<Companion>(); replacementNpc.Bind(player, "bot-replacement-test", "dwarf", "Replacement test", "male", replacement: snapshot);
            } catch (Exception e) { report.Add("FAILED replacement fixture: " + e); }
            yield return new WaitForSecondsRealtime(3);
            try {
                var retained = replacementNpc.Body.GetInventory().GetAllItems().Single(i => i.m_customData.ContainsKey("rune.personal"));
                if (retained.m_customData.ContainsKey("rune.starter")) throw new Exception("Body change reclassified earned equipment as starter gear.");
                report.Add("PASS: Initialized replacement body preserves personal axe without marking it as starter gear.");
                AccessTools.Method(typeof(Companion), "OnDeath").Invoke(replacementNpc, null);
                if (replacementNpc.Count("AxeStone") != 0) throw new Exception("Death retained personal equipment instead of dropping it.");
                report.Add("PASS: Death inventory handler drops personal equipment.");
                ZNetScene.instance.Destroy(replacementNpc.gameObject);
            } catch (Exception e) { report.Add("FAILED replacement/death: " + e); }
            try {
                if (npc.Count("AxeStone") != 1 || npc.CargoCount != 0) throw new Exception("Returning cargo lost the personal axe or failed to deliver stones.");
                report.Add("PASS: Return delivered stone cargo and retained personal axe.");
                var dropIds = UnityEngine.Object.FindObjectsOfType<ItemDrop>().Select(d => d.GetInstanceID()).ToArray();
                npc.DropPersonalEquipment();
                if (npc.Count("AxeStone") != 0 || !UnityEngine.Object.FindObjectsOfType<ItemDrop>().Any(d => !dropIds.Contains(d.GetInstanceID()) && d.m_itemData.m_dropPrefab && d.m_itemData.m_dropPrefab.name == "AxeStone" && !d.m_itemData.m_customData.ContainsKey("rune.personal"))) throw new Exception("Removal did not release personal equipment as an ordinary ground item.");
                report.Add("PASS: Removal releases retained axe onto the ground without personal ownership marker.");
            } catch (Exception e) { report.Add("FAILED retention/removal: " + e); }
            File.WriteAllLines(output, report);
        }
    }
}
