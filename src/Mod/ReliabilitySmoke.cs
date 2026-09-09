using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Rune.Shared;

namespace Rune.Mod
{
    // Entered only through IntegrationSmoke's verified isolated world/save gate.
    internal static class ReliabilitySmoke
    {
        internal static IEnumerator Run(Plugin plugin, Player player, string output)
        {
            var report = new List<string>();
            Companion npc = null; Container chest = null; CraftingStation bench = null;
            // Use the native teleport lifecycle; assigning transform alone can be
            // overwritten by the initial spawn/rigidbody position on the next frame.
            var origin = player.transform.position;
            if (Heightmap.GetHeight(origin, out float ground)) origin.y = ground + 1;
            player.TeleportTo(origin, Quaternion.identity, true);
            yield return new WaitForSecondsRealtime(4);
            object Call(string method, params object[] args) => AccessTools.Method(typeof(Companion), method).Invoke(npc, args);
            void Set(string field, object value) => AccessTools.Field(typeof(Companion), field).SetValue(npc, value);
            void Check(bool ok, string text) { if (!ok) throw new Exception(text); report.Add("PASS: " + text); }
            try {
                var go = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(Plugin.PrefabFor("dwarf")), player.transform.position + Vector3.right * 2, Quaternion.identity);
                npc = go.GetComponent<Companion>(); npc.Bind(player, "reliability-test", "dwarf", "Test", "female");
                chest = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("piece_chest_wood"), player.transform.position + Vector3.right * 3, Quaternion.identity).GetComponent<Container>();
                chest.GetComponent<Piece>().AssignCreator(player.GetPlayerID());
                var benchPoint = player.transform.position + Vector3.forward * 4;
                if (Heightmap.GetHeight(benchPoint, out float floor)) benchPoint.y = floor;
                bench = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("piece_workbench"), benchPoint, Quaternion.identity).GetComponent<CraftingStation>();
                bench.GetComponent<Piece>().AssignCreator(player.GetPlayerID());
                // Isolate item/station selection; shelter is independently covered by construction tests.
                bench.m_craftRequireRoof = false;
            } catch (Exception e) { report.Add("FAILED setup: " + e); }
            yield return new WaitForSecondsRealtime(3);
            try {
                Check(npc && npc.Ready, "Native companion initialized");
                npc.Order("stay", 1); npc.Body.GetInventory().RemoveAll();
                var wood = ZNetScene.instance.GetPrefab("Wood"); chest.GetInventory().RemoveAll(); chest.GetInventory().AddItem(wood, 10);
                npc.UseStoredMaterials = false;
                var item = chest.GetInventory().GetAllItems().First(i => i.m_dropPrefab.name == "Wood");
                bool moved = (bool)Call("Transfer", chest.GetInventory(), npc.Body.GetInventory(), item, 2, chest, null);
                Check(!moved && npc.Count("Wood") == 0 && chest.GetInventory().CountItems(item.m_shared.m_name) == 10, "Disabled chest access preserves both inventories");
                var recipe = ObjectDB.instance.m_recipes.First(r => r && r.m_item && r.m_item.name == "AxeStone");
                AccessTools.Method(typeof(Player), "AddKnownRecipe").Invoke(player, new object[] { recipe });
                foreach (var req in recipe.m_resources.Where(r => r.m_resItem)) npc.Body.GetInventory().AddItem(req.m_resItem.gameObject, req.GetAmount(1));
                npc.Order("craft_item", 1, "stone axe"); Set("craftAt", 0f); Call("TickCraftPlan", .1f);
                Check(npc.Count("AxeStone") == 1 && chest.GetInventory().CountItems(item.m_shared.m_name) == 10, "Crafting consumes carried supplies without chest permission");
                var axe = npc.Body.GetInventory().GetAllItems().First(i => i.m_dropPrefab.name == "AxeStone"); axe.m_durability = axe.GetMaxDurability() * .15f;
                npc.Body.GetInventory().AddItem(ZNetScene.instance.GetPrefab("SwordIron"), 1);
                var sword = npc.Body.GetInventory().GetAllItems().First(i => i.m_dropPrefab.name == "SwordIron");
                sword.m_customData["rune.personal"] = "1"; sword.m_durability = sword.GetMaxDurability() * .01f;
                npc.Order("defend", 1); Set("equipmentCheckAt", 0f); Call("TickEquipmentCare", .1f);
                report.Add("Repair fixture: usable=" + bench.CheckUsable(player, false) + ", accessible=" + Call("StationAccessible", bench) + ", station level=" + bench.GetLevel() + ", selected=" + npc.TaskLabel
                    + ", player distance=" + Vector3.Distance(bench.transform.position, player.transform.position) + ", ward=" + PrivateArea.CheckAccess(bench.transform.position, 0, false, true)
                    + ", safe ground=" + AccessTools.Method(typeof(Companion), "SafeGround").Invoke(null, new object[] { bench.transform.position }) + ", point=" + bench.transform.position);
                if (Physics.Raycast(bench.transform.position + Vector3.up * 4, Vector3.down, out var hit, 12, LayerMask.GetMask("terrain", "static_solid", "Default", "piece"))) report.Add("Station ground ray: " + hit.collider.name + ", slope=" + Vector3.Angle(hit.normal, Vector3.up));
                Check(ReferenceEquals(AccessTools.Field(typeof(Companion), "repairItem").GetValue(npc), axe), "An unavailable forge does not prevent choosing an axe repair at the bench");
                npc.Order("stay", 1); npc.Body.GetInventory().RemoveAll();
                var steps = new[] { new PlanStep { action = "craft_item", item = "stone axe", amount = 1 }, new PlanStep { action = "gather_stone", amount = 2 } };
                npc.QueuePlan(steps, "Make an axe then gather stone"); Call("BlockCraft", "Synthetic missing-supply obstacle");
                string saved = npc.GetComponent<ZNetView>().GetZDO().GetString("rune.savedJob", "");
                var job = WireJson.Read<Companion.SavedJob>(saved);
                Check(job.steps.Length == 2 && job.steps[0].action == "craft_item", "Blocked job preserves current and remaining steps in native save data");
                Set("suspendedJob", null); Call("LoadJob"); npc.AllowCrafting = false;
                npc.Order("resume_task", 1);
                Check(!npc.LastOrderAccepted && npc.Count("AxeStone") == 0, "Resume rechecks changed crafting permissions");
                npc.AllowCrafting = true;
                foreach (var req in recipe.m_resources.Where(r => r.m_resItem)) npc.Body.GetInventory().AddItem(req.m_resItem.gameObject, req.GetAmount(1));
                npc.Order("resume_task", 1); Set("craftAt", 0f); Call("TickCraftPlan", .1f);
                Check(npc.Count("AxeStone") == 1, "Restored task crafts exactly one output from physical ingredients");
                npc.Order("clear_crafting", 1); npc.Order("resume_task", 1);
                Check(!npc.LastOrderAccepted, "Clear crafting removes the saved crafting job");
            } catch (Exception e) { report.Add("FAILED reliability: " + e); }
            File.WriteAllLines(output, report);
            if (npc) ZNetScene.instance.Destroy(npc.gameObject);
            if (chest) ZNetScene.instance.Destroy(chest.gameObject);
            if (bench) ZNetScene.instance.Destroy(bench.gameObject);
        }
    }
}
