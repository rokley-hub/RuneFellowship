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
    internal static class DiagnosticsSmoke
    {
        internal static IEnumerator Run(Plugin plugin, Player player, string output)
        {
            var results = new List<string>();
            var go = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(Plugin.PrefabFor("dwarf")), player.transform.position + Vector3.right * 2, Quaternion.identity);
            var npc = go.GetComponent<Companion>(); npc.Bind(player, "bot-diagnostics", "dwarf", "Diagnostics");
            yield return new WaitForSecondsRealtime(3);
            try {
                Command Make(string action, string item = "") => new Command { id = System.Guid.NewGuid().ToString(), timestamp = Rules.Now, world = Plugin.World, companionId = npc.Id, appearance = "dwarf", displayName = "Diagnostics", action = action, item = item };
                var follow = Make("follow"); var reply = plugin.Execute(follow);
                var state = npc.DiagnosticState();
                if (!reply.accepted || state.commandId != follow.id || state.diagnosticsVersion != 1 || state.task != "Following you") throw new Exception("Follow diagnostic identity mismatch.");
                var decoded = WireJson.Read<CompanionState>(WireJson.Write(state));
                if (decoded.commandId != follow.id || decoded.workProgress.Length == 0 || decoded.x != npc.transform.position.x) throw new Exception("Diagnostic protocol round trip failed.");
                results.Add("PASS: Real accepted command ID, position and inventory progress survive native bridge serialization.");
                plugin.Execute(Make("status"));
                if (npc.DiagnosticCommand != follow.id) throw new Exception("Status replaced tracked job.");
                results.Add("PASS: Status queries preserve the tracked game order.");
                npc.Body.UnequipAllItems(); npc.Body.GetInventory().RemoveAll();
                var recipe = ObjectDB.instance.m_recipes.First(r => r && r.m_item && r.m_item.name == "AxeStone");
                AccessTools.Method(typeof(Player), "AddKnownRecipe").Invoke(player, new object[] { recipe });
                foreach (var req in recipe.m_resources.Where(r => r.m_resItem)) npc.Body.GetInventory().AddItem(req.m_resItem.gameObject, req.GetAmount(1));
                var craft = Make("craft_item", "stone axe"); reply = plugin.Execute(craft);
                if (!reply.accepted || npc.DiagnosticState().outcome != "active") throw new Exception("Craft acceptance incorrectly marked completed.");
                AccessTools.Method(typeof(Companion), "TickCraftPlan").Invoke(npc, new object[] { .1f });
                state = npc.DiagnosticState();
                if (state.commandId != craft.id || state.outcome != "completed" || npc.Count("AxeStone") != 1) throw new Exception("Actual crafted output not reflected as completed.");
                results.Add("PASS: Craft acceptance stays active; actual retained output changes the same command to completed.");
                var failed = Make("craft_item", "stone axe"); plugin.Execute(failed);
                AccessTools.Method(typeof(Companion), "BlockCraft").Invoke(npc, new object[] { "Synthetic inaccessible materials" });
                state = npc.DiagnosticState();
                if (state.outcome != "blocked" || state.commandId != failed.id) throw new Exception("Blocked craft identity/outcome missing.");
                results.Add("PASS: Explicitly blocked crafting retains the command and records a blocked outcome.");
                AccessTools.Field(typeof(Companion), "diagnosticSafety").SetValue(npc, true);
                AccessTools.Field(typeof(Companion), "diagnosticCombat").SetValue(npc, true);
                if (!npc.DiagnosticState().safetyPaused || !npc.DiagnosticState().inCombat) throw new Exception("Pause markers missing.");
                results.Add("PASS: Safety and combat pause markers serialize (synthetically set; not a combat-behavior test).");
                plugin.Execute(Make("stay"));
            } catch (Exception e) { results.Add("FAILED: " + e); }
            File.WriteAllLines(output, results); Application.Quit();
        }
    }
}
