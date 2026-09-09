using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Rune.Mod
{
    internal static class HealthPrioritySmoke
    {
        internal static IEnumerator Run(Plugin plugin, Player player, string output)
        {
            var report = new List<string>();
            var go = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(Plugin.PrefabFor("dwarf")), player.transform.position + Vector3.right * 2, Quaternion.identity);
            var npc = go.GetComponent<Companion>(); npc.Bind(player, "bot-health-test", "dwarf", "Health test", "female", "Balanced", true);
            Character enemy = null;
            yield return new WaitForSecondsRealtime(3);
            try {
                if (!npc.Ready) throw new Exception("NPC not initialized");
                npc.Body.UnequipAllItems(); npc.Body.GetInventory().RemoveAll();
                foreach (var hostile in Character.GetAllCharacters().Where(c => c && BaseAI.IsEnemy(npc.Body, c) && Vector3.Distance(c.transform.position, npc.transform.position) < 30).ToArray()) ZNetScene.instance.Destroy(hostile.gameObject);
                var recipe = ObjectDB.instance.m_recipes.First(r => r && r.m_item && r.m_item.name == "Hammer");
                AccessTools.Method(typeof(Player), "AddKnownRecipe").Invoke(player, new object[] { recipe });
                foreach (var req in recipe.m_resources.Where(r => r.m_resItem)) npc.Body.GetInventory().AddItem(req.m_resItem.gameObject, req.GetAmount(1));
                player.SetHealth(player.GetMaxHealth()); npc.Body.SetHealth(54.22f);
                AccessTools.Field(typeof(Companion), "recovering").SetValue(npc, true);
                npc.Order("craft_item", 1, "Hammer");
                npc.SetObjective("Craft a hammer for personal use", "craft_item", 1, "Hammer"); npc.DiagnosticAction = "craft_item";
                var questLines = AccessTools.Method(typeof(Plugin), "QuestLines");
                var statusLines = AccessTools.Method(typeof(Plugin), "StatusLines");
                var activeStatus = (string[])statusLines.Invoke(plugin, new object[] { npc });
                if (!activeStatus.Any(s => s == "GOAL · Craft a hammer for personal use") || !activeStatus.Any(s => s.StartsWith("NOW · "))) throw new Exception("Overlay goal/current-state separation missing.");
                if (((string[])questLines.Invoke(plugin, new object[] { npc })).Length == 0) throw new Exception("Active quest missing from overlay.");
                npc.Tick(.1f);
                if (npc.Count("Hammer") != 1 || npc.DiagnosticState().safetyPaused || npc.QuestSnapshot().status != "Complete") throw new Exception("Recovery still prevented safe real-ingredient hammer crafting.");
                report.Add("PASS: At the recorded 54.22/150 HP, recovery permits crafting an actual hammer in a safe area.");
                if (((string[])questLines.Invoke(plugin, new object[] { npc })).Length != 0) throw new Exception("Completed quest still shown in overlay.");
                if (((string[])statusLines.Invoke(plugin, new object[] { npc })).Any(s => s.StartsWith("GOAL · "))) throw new Exception("Completed objective still shown in overlay.");
                npc.Order("clear_crafting", 1);
                if (((string[])questLines.Invoke(plugin, new object[] { npc })).Length != 0) throw new Exception("Cleared quest still shown in overlay.");
                report.Add("PASS: Overlay includes active crafting and hides completed and cleared checklists.");
                var axe = ZNetScene.instance.GetPrefab("AxeStone").GetComponent<ItemDrop>().m_itemData.Clone(); axe.m_dropPrefab = ZNetScene.instance.GetPrefab("AxeStone"); axe.m_customData["rune.personal"] = "1";
                var sword = ZNetScene.instance.GetPrefab("SwordIron").GetComponent<ItemDrop>().m_itemData.Clone(); sword.m_dropPrefab = ZNetScene.instance.GetPrefab("SwordIron"); sword.m_customData["rune.personal"] = "1";
                npc.Body.GetInventory().AddItem(axe); npc.Body.GetInventory().AddItem(sword); npc.Body.EquipItem(axe, false);
                npc.Order("defend", 20);
                Vector3 point = player.transform.position + Vector3.forward * 5;
                if (Heightmap.GetHeight(point, out float ground)) point.y = ground + .2f;
                enemy = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Greydwarf"), point, Quaternion.identity).GetComponent<Character>();
                // The fixture checks real NPC routing; a disabled enemy AI prevents random damage
                // affecting controlled HP assertions. It does not test a successful player rescue.
                enemy.GetComponent<MonsterAI>().enabled = false; enemy.SetMaxHealth(5000); enemy.SetHealth(5000);
            } catch (Exception e) { report.Add("FAILED setup/work: " + e); }
            yield return new WaitForSecondsRealtime(.5f);
            try {
                if (!enemy || !npc) throw new Exception("Missing combat fixture");
                // Spawn terrain/intro settling can put the player well above the enemy.
                // Place the controlled three-character scene together before health assertions.
                bool safePlayer = false;
                for (int i = 0; i < 24; i++) {
                    Vector3 spot = enemy.transform.position + Quaternion.Euler(0, i * 15, 0) * Vector3.forward * 3;
                    if (Heightmap.GetHeight(spot, out float height)) spot.y = height + .2f;
                    if (!(bool)AccessTools.Method(typeof(Companion), "SafeGround").Invoke(null, new object[] { spot })) continue;
                    player.transform.position = spot; safePlayer = true; break;
                }
                if (!safePlayer) throw new Exception("Fixture has no safe player destination near the enemy.");
                npc.transform.position = enemy.transform.position - Vector3.right * 2;
                if (Vector3.Distance(player.transform.position, enemy.transform.position) >= 14) throw new Exception("Fixture threat is outside player-protection range.");
                player.SetHealth(player.GetMaxHealth()); npc.Body.SetHealth(45); npc.Tick(.1f);
                if (!npc.DiagnosticState().safetyPaused || !npc.TaskLabel.Contains("combat ready at 82.5")) throw new Exception("Ordinary injured-companion recovery/visible target missing.");
                report.Add("PASS: With a healthy player, a hurt Balanced companion pauses combat and reports the 82.5 HP recovery target.");
                player.SetHealth(player.GetMaxHealth() * .2f); npc.Body.SetHealth(5);
                bool nativeCombat = npc.Tick(.1f);
                if (!nativeCombat || npc.DiagnosticState().safetyPaused || !npc.DiagnosticState().inCombat || npc.GetComponent<MonsterAI>().GetTargetCreature() != enemy || !npc.TaskLabel.Contains("Covering you")) throw new Exception("Critical companion HP prevented player protection. native=" + nativeCombat + " safety=" + npc.DiagnosticState().safetyPaused + " combat=" + npc.DiagnosticState().inCombat + " target=" + npc.GetComponent<MonsterAI>().GetTargetCreature() + " status=" + npc.TaskLabel + " playerHP=" + player.GetHealthPercentage() + " safeTarget=" + AccessTools.Method(typeof(Companion), "SafeGround").Invoke(null, new object[] { enemy.transform.position }) + " distance=" + Vector3.Distance(player.transform.position, enemy.transform.position));
                if (npc.Body.GetCurrentWeapon()?.m_dropPrefab?.name != "SwordIron") throw new Exception("Combat selected work axe instead of available stronger weapon.");
                report.Add("PASS: At 5/150 HP, companion chooses native combat against the player's nearby threat and equips the sword instead of the work axe.");
                player.SetHealth(player.GetMaxHealth() * .5f); npc.Tick(.1f);
                if (!npc.TaskLabel.Contains("Covering you")) throw new Exception("Protection oscillated after a small player heal.");
                report.Add("PASS: Protection persists while player recovers to 50 percent.");
                player.SetHealth(player.GetMaxHealth()); npc.Tick(.1f);
                if (!npc.DiagnosticState().safetyPaused) throw new Exception("Normal companion recovery did not resume after player became safe.");
                npc.Body.SetHealth(83); npc.Tick(.1f);
                if (npc.DiagnosticState().safetyPaused) throw new Exception("Balanced companion waited beyond new resume threshold.");
                report.Add("PASS: Healthy player restores ordinary recovery; Balanced combat resumes at 83/150 HP.");
                player.SetHealth(player.GetMaxHealth() * .2f); npc.Body.SetHealth(5); npc.Order("follow", 20); npc.Tick(.1f);
                if (npc.GetComponent<MonsterAI>().GetTargetCreature() || npc.TaskLabel.Contains("Covering you") || npc.DiagnosticState().safetyPaused) throw new Exception("Follow failed to override protection and personal recovery: " + npc.TaskLabel + " target=" + npc.GetComponent<MonsterAI>().GetTargetCreature());
                report.Add("PASS: Explicit follow recalls the companion even when both player and companion are hurt.");
                player.SetHealth(player.GetMaxHealth()); npc.Body.SetHealth(48); npc.UpdateIdentity("Health test", "female", "Aggressive", true);
                AccessTools.Field(typeof(Companion), "recovering").SetValue(npc, false); npc.Order("defend", 20); npc.Tick(.1f);
                if (npc.DiagnosticState().safetyPaused) throw new Exception("The obsolete 35 percent global guard still blocks Aggressive behavior.");
                report.Add("PASS: Aggressive combat at 32 percent HP is no longer blocked by a separate 35 percent guard.");
            } catch (Exception e) { report.Add("FAILED combat: " + e); }
            File.WriteAllLines(output, report); Application.Quit();
        }
    }
}
