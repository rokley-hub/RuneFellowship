using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace Rune.Mod
{
    internal static class SuppliesSmoke
    {
        internal static IEnumerator Run(Plugin plugin, Player player, string output)
        {
            var report = new List<string>(); Companion npc = null; ItemDrop drop = null;
            void Check(bool ok, string note) { if (!ok) throw new Exception(note); report.Add("PASS: " + note); File.WriteAllLines(output, report); }
            void Field(string name, object value) => AccessTools.Field(typeof(Companion), name).SetValue(npc, value);
            object Get(string name) => AccessTools.Field(typeof(Companion), name).GetValue(npc);
            object Call(string name, params object[] args) => AccessTools.Method(typeof(Companion), name).Invoke(npc, args);
            try {
                foreach (var c in Character.GetAllCharacters().ToArray()) if (c && !(c is Player) && !c.GetComponent<Companion>()) UnityEngine.Object.Destroy(c.gameObject);
                var go = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(Plugin.PrefabFor("dwarf")), player.transform.position + Vector3.right * 2, Quaternion.identity);
                npc = go.GetComponent<Companion>(); npc.Bind(player, "bot-supplies-test", "dwarf", "Supplies test", "female");
            } catch (Exception ex) { report.Add("FAILED setup: " + ex); File.WriteAllLines(output, report); }
            yield return new WaitForSecondsRealtime(3);
            ItemDrop.ItemData Add(string prefab, int amount = 1) {
                var go = ZNetScene.instance.GetPrefab(prefab); var item = go.GetComponent<ItemDrop>().m_itemData.Clone(); item.m_dropPrefab = go; item.m_stack = amount; item.m_durability = item.GetMaxDurability();
                if (!npc.Body.GetInventory().AddItem(item)) throw new Exception("Fixture inventory full: " + prefab); return item;
            }
            try {
                Check(npc && npc.Ready, "Companion is ready in an isolated native game world.");
                npc.Order("stay", 20); npc.Body.UnequipAllItems(); npc.Body.GetInventory().RemoveAll(); player.GetInventory().RemoveAll();
                var bow = Add("Bow"); bow.m_customData["rune.personal"] = "1"; npc.Body.EquipItem(bow, false);
                Add("ArrowWood", 30); var axe = Add("AxeStone"); Add("Wood", 10);
                Call("RefreshSupplies");
                Check(npc.CargoCount == 10 && axe.m_customData.ContainsKey("rune.supply"), "Own axe and bow ammunition are protected; ordinary wood remains cargo.");
                Check(npc.EquippedWeaponStatus.Contains("30 arrows"), "HUD shows actual matching bow ammunition.");
                Call("Deliver");
                Check(npc.Count("ArrowWood") == 30 && npc.Count("AxeStone") == 1 && player.GetInventory().CountItems("$item_wood") == 10 && npc.Count("Wood") == 0, "Cargo delivery keeps essential tools/arrows and transfers wood without losing items.");
                npc.Body.UnequipItem(bow, false); npc.Body.GetInventory().RemoveItem(bow); Call("RefreshSupplies");
                Check(npc.CargoCount == 30, "Ammunition becomes ordinary cargo when the matching personal bow is removed.");
                var package = new ZPackage(); npc.Body.GetInventory().Save(package); var restored = new Inventory("fixture", null, 8, 4); restored.Load(new ZPackage(package.GetArray()));
                Check(restored.GetAllItems().Any(i => i.m_customData.ContainsKey("rune.supply")), "Native inventory serialization preserves supply protection.");
                npc.Order("stay", 20); npc.Body.UnequipAllItems(); npc.Body.GetInventory().RemoveAll();
                Add("Wood", 49); while (npc.Body.GetInventory().GetEmptySlots() > 0) Add("HelmetLeather");
                Field("resource", "Wood"); Field("goal", 10); Field("gathered", 0);
                Check((bool)Call("HasGatherCapacity"), "All slots occupied still allows wood into a partial existing stack.");
                var dropObject = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Wood"), npc.transform.position + Vector3.up, Quaternion.identity);
                drop = dropObject.GetComponent<ItemDrop>(); drop.m_itemData.m_stack = 5;
            } catch (Exception ex) { report.Add("FAILED: " + ex); File.WriteAllLines(output, report); yield break; }
            yield return new WaitForSecondsRealtime(2);
            try {
                Field("resource", "Wood"); Field("goal", 10); Field("gathered", 0);
                Call("TakeDrop", drop);
                Check(npc.Count("Wood") == 50 && drop.m_itemData.m_stack == 4 && (int)Get("gathered") == 1, "Pickup fills the one available stack space and leaves the other four wood in the world.");
                Check(!(bool)Call("HasGatherCapacity"), "A fully filled stack with no free slots reports no capacity.");
                Call("ResetGatherRecovery"); Check((bool)Call("WaitForGatherRecovery"), "Missing targets enter a bounded retry period.");
                float deadline = (float)Get("gatherRecoveryDeadline"); Call("WaitForGatherRecovery");
                Check((float)Get("gatherRecoveryDeadline") == deadline && npc.TaskLabel.Contains("Checking for another"), "Repeated scans preserve the same deadline and expose retry activity in the HUD.");
                Field("gatherRecoveryDeadline", Time.time - 1); Check(!(bool)Call("WaitForGatherRecovery"), "Retry expiry returns control to the normal explained blocker path.");
                npc.Order("follow", 20); Check((float)Get("gatherRecoveryDeadline") == 0 && (string)Get("gatherRecoveryNote") == "" && (bool)Get("followingOrder"), "Follow immediately cancels retry state.");
                var worn = npc.Body.GetInventory().GetAllItems().First(); Field("repairItem", worn); Field("repairStarted", Time.time - 20); Field("orderTime", Time.time - 30);
                Call("CancelMaintenance"); Check(Time.time - (float)Get("orderTime") < 11 && Get("repairItem") == null, "Interrupted repair detours do not consume the gathering timeout budget.");
            } catch (Exception ex) { report.Add("FAILED: " + ex); }
            File.WriteAllLines(output, report);
            if (npc) UnityEngine.Object.Destroy(npc.gameObject);
            if (drop) UnityEngine.Object.Destroy(drop.gameObject);
        }
    }
}
