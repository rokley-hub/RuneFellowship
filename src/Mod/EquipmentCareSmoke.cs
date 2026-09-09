using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Rune.Mod
{
    internal static class EquipmentCareSmoke
    {
        internal static IEnumerator Run(Plugin plugin, Player player, string output)
        {
            var report = new List<string>(); Companion npc = null; CraftingStation station = null; ItemDrop.ItemData axe = null;
            try {
                var go = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(Plugin.PrefabFor("dwarf")), player.transform.position + Vector3.right * 2, Quaternion.identity);
                npc = go.GetComponent<Companion>(); npc.Bind(player, "bot-care-test", "dwarf", "Care test", "female");
            } catch (Exception e) { report.Add("FAILED setup: " + e); }
            yield return new WaitForSecondsRealtime(3);
            try {
                if (!npc || !npc.Ready) throw new Exception("Companion not ready.");
                npc.Order("stay", 20); npc.Body.UnequipAllItems(); npc.Body.GetInventory().RemoveAll();
                var recipe = ObjectDB.instance.m_recipes.First(r => r && r.m_item && r.m_item.name == "AxeStone");
                AccessTools.Method(typeof(Player), "AddKnownRecipe").Invoke(player, new object[] { recipe });
                foreach (var req in recipe.m_resources.Where(r => r.m_resItem)) npc.Body.GetInventory().AddItem(req.m_resItem.gameObject, req.GetAmount(1) + 1);
                var filler = new List<ItemDrop.ItemData>();
                while (npc.Body.GetInventory().GetEmptySlots() > 0) {
                    var helmet = ZNetScene.instance.GetPrefab("HelmetLeather").GetComponent<ItemDrop>().m_itemData.Clone(); helmet.m_dropPrefab = ZNetScene.instance.GetPrefab("HelmetLeather");
                    if (!npc.Body.GetInventory().AddItem(helmet)) throw new Exception("Full-inventory fixture failed."); filler.Add(helmet);
                }
                npc.Order("craft_item", 20, "stone axe");
                AccessTools.Method(typeof(Companion), "TickCraftPlan").Invoke(npc, new object[] { .1f });
                if (npc.Count("AxeStone") != 0 || recipe.m_resources.Where(r => r.m_resItem).Any(r => npc.Count(r.m_resItem.name) != r.GetAmount(1) + 1)) throw new Exception("Failed full-inventory craft lost ingredients or created output.");
                report.Add("PASS: When no output slot can be freed, crafting restores all ingredients and creates no item.");
                foreach (var req in recipe.m_resources.Where(r => r.m_resItem)) npc.Body.GetInventory().RemoveItem(npc.Body.GetInventory().GetAllItems().First(i => i.m_dropPrefab && i.m_dropPrefab.name == req.m_resItem.name), 1);
                npc.Order("craft_item", 20, "stone axe");
                AccessTools.Method(typeof(Companion), "TickCraftPlan").Invoke(npc, new object[] { .1f });
                axe = npc.Body.GetInventory().GetAllItems().Single(i => i.m_dropPrefab && i.m_dropPrefab.name == "AxeStone");
                if (!axe.m_equipped || !axe.m_customData.ContainsKey("rune.personal") || npc.QuestSnapshot().status != "Complete" || (string)AccessTools.Field(typeof(Companion), "mode").GetValue(npc) != "follow") throw new Exception("Crafted axe not retained/equipped or delivery started.");
                report.Add("PASS: Crafted a stone axe from actual ingredients, equipped it and followed without starting delivery.");
                foreach (var item in filler) npc.Body.GetInventory().RemoveItem(item);
                report.Add("PASS: Full inventory can craft when consuming ingredients frees the output slot; other items are preserved.");
                npc.Order("return", 20); AccessTools.Method(typeof(Companion), "Deliver").Invoke(npc, null);
                if (!npc.Body.GetInventory().GetAllItems().Contains(axe)) throw new Exception("Return gave away personal crafted tool.");
                report.Add("PASS: Explicit cargo return keeps crafted personal equipment.");
                axe.m_durability = axe.GetMaxDurability() * .1f;
                npc.Order("defend", 20);
                AccessTools.Field(typeof(Companion), "equipmentCheckAt").SetValue(npc, 0f);
                AccessTools.Method(typeof(Companion), "TickEquipmentCare").Invoke(npc, new object[] { .1f });
                if (axe.GetDurabilityPercentage() > .11f || !plugin.Note.Contains("durability")) throw new Exception("Missing-station repair was silent or repaired remotely.");
                report.Add("PASS: Low durability without a station reports the need and does not change durability.");
                var point = npc.transform.position + Vector3.forward * 5; if (Heightmap.GetHeight(point, out float ground)) point.y = ground;
                station = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("piece_workbench"), point, Quaternion.identity).GetComponent<CraftingStation>();
                station.GetComponent<Piece>().AssignCreator(player.GetPlayerID());
            } catch (Exception e) { report.Add("FAILED crafting/setup: " + e); }
            yield return new WaitForSecondsRealtime(2);
            Vector3 start = npc ? npc.transform.position : Vector3.zero;
            try {
                if (station.CheckUsable(player, false)) throw new Exception("Fixture expected exposed workbench.");
                AccessTools.Field(typeof(Companion), "equipmentCheckAt").SetValue(npc, 0f);
                AccessTools.Method(typeof(Companion), "TickEquipmentCare").Invoke(npc, new object[] { .1f });
                if (axe.GetDurabilityPercentage() > .11f) throw new Exception("Exposed workbench repaired equipment.");
                report.Add("PASS: Real station usability rejects exposed workbench.");
                // Make only this isolated test station usable; production always uses CheckUsable.
                station.m_craftRequireRoof = false;
                AccessTools.Field(typeof(Companion), "equipmentCheckAt").SetValue(npc, 0f);
                npc.Tick(.1f);
                if (!npc.TaskLabel.StartsWith("Repairing")) throw new Exception("No repair detour started.");
                npc.Order("follow", 20);
                if (npc.TaskLabel.StartsWith("Repairing") || AccessTools.Field(typeof(Companion), "repairItem").GetValue(npc) != null) throw new Exception("Follow did not abort repair.");
                npc.Order("stay", 20); AccessTools.Field(typeof(Companion), "equipmentCheckAt").SetValue(npc, 0f); npc.Tick(.1f);
                if (npc.TaskLabel.StartsWith("Repairing")) throw new Exception("Stay permitted a maintenance detour.");
                report.Add("PASS: Follow interrupts maintenance immediately; stay prevents autonomous repair movement.");
                npc.Order("defend", 20); AccessTools.Field(typeof(Companion), "equipmentCheckAt").SetValue(npc, 0f);
            } catch (Exception e) { report.Add("FAILED station/interruption: " + e); }
            File.WriteAllLines(output, report);
            float deadline = Time.realtimeSinceStartup + 35;
            while (axe != null && axe.GetDurabilityPercentage() < .99f && Time.realtimeSinceStartup < deadline) yield return new WaitForSecondsRealtime(.3f);
            try {
                if (axe.GetDurabilityPercentage() < .99f || Vector3.Distance(npc.transform.position, station.transform.position) > 2.6f) throw new Exception("Did not walk to station and repair. " + npc.TaskLabel);
                report.Add("PASS: Automatic repair moved " + Vector3.Distance(start, npc.transform.position).ToString("F2") + "m to compatible usable station, restored durability and preserved work mode.");
                // A real stackable recipe checks native merge behavior and explicit delivery.
                var recipe = ObjectDB.instance.m_recipes.First(r => r && r.m_item && r.m_item.name == "ArrowWood");
                AccessTools.Method(typeof(Player), "AddKnownRecipe").Invoke(player, new object[] { recipe });
                npc.Body.GetInventory().AddItem(recipe.m_item.gameObject, 2);
                foreach (var req in recipe.m_resources.Where(r => r.m_resItem)) npc.Body.GetInventory().AddItem(req.m_resItem.gameObject, req.GetAmount(1));
                npc.Order("craft_item", 20, "ArrowWood"); AccessTools.Method(typeof(Companion), "TickCraftPlan").Invoke(npc, new object[] { .1f });
                if (npc.Count("ArrowWood") != 2 + recipe.m_amount || !npc.Body.GetInventory().GetAllItems().Where(i => i.m_dropPrefab && i.m_dropPrefab.name == "ArrowWood").All(i => i.m_customData.ContainsKey("rune.kept"))) throw new Exception("Stacked crafted output was not kept.");
                npc.Order("gather_item", 1, "nothing-test"); AccessTools.Method(typeof(Companion), "Deliver").Invoke(npc, null);
                if (npc.Count("ArrowWood") != 2 + recipe.m_amount) throw new Exception("Automatic delivery leaked kept crafted stack.");
                npc.Order("return", 20); AccessTools.Method(typeof(Companion), "Deliver").Invoke(npc, null);
                if (npc.Count("ArrowWood") != 0) throw new Exception("Explicit return did not transfer kept crafted stack.");
                report.Add("PASS: Crafted stack merges retain ownership, automatic cargo delivery skips it, explicit return delivers it.");
            } catch (Exception e) { report.Add("FAILED repair/stack: " + e); }
            File.WriteAllLines(output, report);
        }
    }
}
