using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Rune.Mod
{
    internal static class WorkbenchSmoke
    {
        internal static IEnumerator Run(Plugin plugin, Player player, string output)
        {
            var report = new List<string>(); Companion npc = null; Vector3 origin = Vector3.zero, initialNpc = Vector3.zero;
            bool started = false; int required = 0, before = 0; float hammerDurability = 0;
            Func<Piece[]> benches = () => UnityEngine.Object.FindObjectsOfType<Piece>().Where(p => p.name.StartsWith("piece_workbench") && !p.GetComponent(PlanBuildAdapter.PlanType) && p.GetCreator() == player.GetPlayerID()).ToArray();
            try {
                var go = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(Plugin.PrefabFor("dwarf")), player.transform.position + Vector3.right * 2, Quaternion.identity);
                npc = go.GetComponent<Companion>(); npc.Bind(player, "bench-test", "dwarf", "Bench test", "female", "Balanced", true);
            } catch (Exception e) { report.Add("FAILED spawn: " + e); }
            yield return new WaitForSecondsRealtime(3);
            try {
                if (!npc || !npc.Ready) throw new Exception("Companion not ready");
                npc.Body.GetInventory().RemoveAll();
                var models = ZNetScene.instance.GetPrefab("Hammer").GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces.m_pieces;
                File.WriteAllLines(Path.Combine(Path.GetDirectoryName(output), "building-prefabs.txt"), models.Where(g => g && (g.name.StartsWith("wood_") || g.name.Contains("bed") || g.name.Contains("fire") || g.name.Contains("workbench"))).Select(g => g.name + " | " + string.Join(";", g.GetComponentsInChildren<BoxCollider>().Select(c => "box=" + g.transform.InverseTransformPoint(c.transform.TransformPoint(c.center)) + " size=" + c.size + " scale=" + c.transform.lossyScale)) + " | " + string.Join(",",g.GetComponent<Piece>().m_resources.Where(r=>r.m_resItem).Select(r=>r.m_resItem.name+":"+r.GetAmount(1)))));
                var prefab = ZNetScene.instance.GetPrefab("piece_workbench"); var piece = prefab.GetComponent<Piece>();
                required = piece.m_resources.Single(r => r.m_resItem && r.m_resItem.name == "Wood").GetAmount(1);
                AccessTools.Method(typeof(Player), "AddKnownItem").Invoke(player, new object[] { ZNetScene.instance.GetPrefab("Wood").GetComponent<ItemDrop>().m_itemData });
                ((HashSet<string>)AccessTools.Field(typeof(Player), "m_knownRecipes").GetValue(player)).Remove(piece.m_name);
                if (player.IsRecipeKnown(piece.m_name) || !BuildingKnowledge.Known(player, piece)) throw new Exception("Known wood with an absent recipe-cache entry rejected");
                report.Add("PASS: Workbench discovery uses real learned materials with no forced AddKnownPiece.");
                var blueprintType = AccessTools.TypeByName("PlanBuild.Blueprints.Blueprint");
                object blueprint = AccessTools.Method(blueprintType, "FromArray").Invoke(null, new object[] { "rune-native-test", new[] { "#Name:Rune native test", "#Pieces", "piece_workbench;Crafting;0;0;0;0;0;0;1;;1;1;1" }, Enum.Parse(blueprintType.GetNestedType("Format"), "Blueprint") });
                var blueprintManager = AccessTools.TypeByName("PlanBuild.Blueprints.BlueprintManager");
                ((IDictionary)AccessTools.Field(blueprintManager, "LocalBlueprints").GetValue(null))["rune-native-test"] = blueprint;
                var onPlayer = PlanBuildAdapter.Resolve("Rune native test", player.transform.position, Quaternion.identity);
                var onSelf = PlanBuildAdapter.Resolve("rune native test", npc.transform.position, Quaternion.identity);
                if (onPlayer.Count != 1 || onPlayer[0].Position != player.transform.position || onSelf[0].Position != npc.transform.position) throw new Exception("Named blueprint did not use its supplied anchor");
                bool unknownRejected = false; try { PlanBuildAdapter.Resolve("does not exist", player.transform.position, Quaternion.identity); } catch (InvalidOperationException) { unknownRejected = true; }
                if (!unknownRejected) throw new Exception("Unknown blueprint was accepted");
                report.Add("PASS: Real PlanBuild blueprint parser resolves saved name, player/self anchors, and rejects unknown names.");
                string missing = npc.Order("craft_item", 1, "workbench");
                if (npc.LastOrderAccepted || !missing.Contains("hammer")) throw new Exception("Missing hammer was not rejected explicitly: " + missing);
                report.Add("PASS: Missing hammer rejects workbench order with an explicit explanation.");
                npc.Body.GetInventory().AddItem(ZNetScene.instance.GetPrefab("Hammer"), 1);
                hammerDurability = npc.Body.GetInventory().GetAllItems().Single().m_durability;
                var seed = player.transform.position;
                for (int x = 0; x <= 12 && !started; x += 4) for (int z = 0; z <= 12 && !started; z += 4) {
                    var at = seed + new Vector3(x, 0, z); if (!Heightmap.GetHeight(at, out float height) || height < 31) continue;
                    at.y = height + .2f; player.transform.position = at; npc.transform.position = at + Vector3.right * 2;
                    string response = npc.Order("craft_item", 1, "work bench");
                    if (!npc.LastOrderAccepted) continue;
                    AccessTools.Field(typeof(Companion), "craftAt").SetValue(npc, Time.time + 10); origin = at; initialNpc = npc.transform.position; started = true;
                    report.Add("Started: " + response);
                }
                if (!started) throw new Exception("Could not find an acceptable test site in loaded terrain");
                foreach (var hostile in Character.GetAllCharacters().Where(c => c && BaseAI.IsEnemy(npc.Body, c) && Vector3.Distance(c.transform.position, origin) < 50).ToArray()) ZNetScene.instance.Destroy(hostile.gameObject);
                var planned = UnityEngine.Object.FindObjectsOfType(PlanBuildAdapter.PlanType).Cast<Component>().Single(p => p.GetComponent<Piece>().GetCreator() == player.GetPlayerID());
                var wood = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Wood"), planned.transform.position + Vector3.up * .3f, Quaternion.identity).GetComponent<ItemDrop>();
                // Stable fixture resource: stop it rolling down the surrounding mountain.
                wood.GetComponent<Rigidbody>().constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
                wood.m_itemData.m_dropPrefab = ZNetScene.instance.GetPrefab("Wood");
                wood.m_itemData.m_stack = required; AccessTools.Method(typeof(ItemDrop), "Save").Invoke(wood, null);
                report.Add("Fixture loose wood at " + wood.transform.position + "; bench plan=" + planned.transform.position + "; reference=" + origin);
                if (npc.QuestSnapshot().materials.Single().required != required || npc.QuestSnapshot().gather.All(n => n.prefab != "Wood")) throw new Exception("Workbench gathering checklist missing live wood recipe: " + WireJson.Write(npc.QuestSnapshot()));
                report.Add("PASS: Active workbench checklist uses the live wood requirement and missing-material list.");
                before = benches().Length;
                // Move the player away after requesting: destination should stay at the request site.
                var moved = origin + Vector3.back * 12; if (Heightmap.GetHeight(moved, out float newGround)) moved.y = newGround + .2f;
                // Relocate the fixture player only; companion movement is always native.
                // Background game input can leave a player teleport pending indefinitely.
                AccessTools.Field(typeof(Player), "m_teleporting").SetValue(player, false);
                player.transform.position = moved; player.GetComponent<Rigidbody>().position = moved;
                AccessTools.Field(typeof(Companion), "craftAt").SetValue(npc, Time.time + 15);
                AccessTools.Method(typeof(Plugin), "WriteRecipeCatalog").Invoke(plugin, null);
                if (!File.ReadAllText(Path.Combine(Plugin.BridgePath, "recipes.json")).Contains("piece_workbench")) throw new Exception("Workbench absent from model recipe catalog");
                var book = WireJson.Read<Rune.Shared.RecipeBook>(File.ReadAllText(Path.Combine(Plugin.BridgePath, "recipes.json")));
                if (!book.recipes.Single(r => r.prefab == "piece_workbench").known) throw new Exception("Exported workbench knowledge disagrees with executor");
                report.Add("PASS: Recipe catalog advertises the workbench to both model routes.");
            } catch (Exception e) { report.Add("FAILED setup: " + e); started = false; }
            float until = Time.realtimeSinceStartup + 120, logAt = 0;
            while (started && npc.QuestSnapshot()?.status == "Active" && Time.realtimeSinceStartup < until) {
                player.SetHealth(player.GetMaxHealth());
                foreach (var hostile in Character.GetAllCharacters().Where(c => c && BaseAI.IsEnemy(npc.Body, c) && Vector3.Distance(c.transform.position, origin) < 70).ToArray()) ZNetScene.instance.Destroy(hostile.gameObject);
                if (Time.realtimeSinceStartup >= logAt) { logAt = Time.realtimeSinceStartup + 5; var current = (ResourceTarget)AccessTools.Field(typeof(Companion), "target").GetValue(npc); report.Add("Progress: " + npc.TaskLabel + " wood=" + npc.Count("Wood") + " position=" + npc.transform.position + "; target=" + (current == null ? "none" : current.Component.name + " at " + current.Position)); File.WriteAllLines(output, report); }
                yield return new WaitForSecondsRealtime(.25f);
            }
            try {
                if (!started || npc.QuestSnapshot()?.status != "Complete") throw new Exception("Workbench did not complete: " + npc?.TaskLabel + " " + npc?.QuestSnapshot()?.note);
                var placed = benches(); if (placed.Length != before + 1) throw new Exception("Expected exactly one placed workbench");
                var bench = placed.Last(); if (Vector3.Distance(bench.transform.position, origin) > 6.5f) throw new Exception("Workbench followed player away from the original site");
                if (Vector3.Distance(npc.transform.position, initialNpc) < .3f) throw new Exception("Companion did not physically move");
                if (npc.Count("Wood") != 0 || npc.Count("Hammer") != 1) throw new Exception("Ingredients were not consumed exactly or hammer was lost");
                var hammer = npc.Body.GetInventory().GetAllItems().Single(i => i.m_dropPrefab.name == "Hammer");
                if (hammer.m_durability >= hammerDurability) throw new Exception("Building did not use hammer durability");
                report.Add("PASS: Native companion gathered wood, walked, consumed exact ingredients, used hammer durability and placed exactly one networked workbench within six metres of the original request.");
                if ((bool)AccessTools.Method(typeof(Companion), "WorkbenchSpace").Invoke(null, new object[] { bench.transform.position, bench.transform.rotation })) throw new Exception("Existing workbench failed overlap rejection");
                if ((bool)AccessTools.Method(typeof(Companion), "ConstructionSpace").Invoke(null, new object[] { ZNetScene.instance.GetPrefab("piece_workbench").GetComponent<Piece>(), bench.transform.position, bench.transform.rotation, null })) throw new Exception("PlanBuild collision check allowed a duplicate bench");
                if ((bool)AccessTools.Method(typeof(Companion), "WorkbenchSpace").Invoke(null, new object[] { new Vector3(bench.transform.position.x, 20, bench.transform.position.z), Quaternion.identity })) throw new Exception("Unsupported underwater spot accepted");
                report.Add("PASS: Occupied and unsupported/water placement spots rejected.");
                report.Add("Native station usable=" + bench.GetComponent<CraftingStation>().CheckUsable(player, false) + "; no shelter spawned.");
                npc.Body.GetInventory().AddItem(ZNetScene.instance.GetPrefab("Wood"), required);
                bool accepted = false;
                for (int x = -8; x <= 8 && !accepted; x += 2) for (int z = -8; z <= 8 && !accepted; z += 2) {
                    var site = bench.transform.position + new Vector3(x, 0, z); if (Vector3.Distance(site, bench.transform.position) < 4 || !Heightmap.GetHeight(site, out float ground) || ground < 31) continue;
                    site.y = ground;
                    if (!(bool)AccessTools.Method(typeof(Companion), "ConstructionSpace").Invoke(null, new object[] { ZNetScene.instance.GetPrefab("piece_workbench").GetComponent<Piece>(), site, Quaternion.identity, null })) continue;
                    npc.transform.position = site; npc.Order("planbuild_self", 1, "Rune native test"); accepted = npc.LastOrderAccepted;
                }
                if (!accepted) throw new Exception("No clear second blueprint fixture position");
                npc.Order("follow", 1); npc.Tick(.1f);
                if (AccessTools.Field(typeof(Companion), "craftPlan").GetValue(npc) != null || benches().Length != before + 1 || npc.Count("Wood") != required) throw new Exception("Follow failed to cancel without consuming materials");
                report.Add("PASS: Follow cancels pending workbench placement without consuming materials or placing a second bench.");
            } catch (Exception e) { report.Add("FAILED behavior: " + e); }
            File.WriteAllLines(output, report);
            if (started && !report.Any(line => line.StartsWith("FAILED"))) yield return MultiPiece(plugin, player, npc, report, output);
            Application.Quit();
        }
        private static IEnumerator MultiPiece(Plugin plugin, Player player, Companion npc, List<string> report, string output)
        {
            bool running = false; int before = 0; Vector3 first = Vector3.zero, second = Vector3.zero;
            try {
                var pending = UnityEngine.Object.FindObjectsOfType(PlanBuildAdapter.PlanType).Cast<Component>().Where(p => p.GetComponent<Piece>().GetCreator() == player.GetPlayerID()).ToArray();
                first = pending.Single().transform.position;
                foreach (var plan in pending) ZNetScene.instance.Destroy(plan.gameObject);
                var previousBench = UnityEngine.Object.FindObjectsOfType<Piece>().Single(p => p.name.StartsWith("piece_workbench") && !p.GetComponent(PlanBuildAdapter.PlanType) && p.GetCreator() == player.GetPlayerID());
                second = previousBench.transform.position;
                ZNetScene.instance.Destroy(previousBench.gameObject);
                report.Add("Multi-piece fixture resets the two proven clear sites after the single-piece assertions.");
            } catch (Exception e) { report.Add("FAILED multi-piece fixture reset: " + e); }
            yield return new WaitForSeconds(2);
            try {
                var type = AccessTools.TypeByName("PlanBuild.Blueprints.Blueprint"); var entryType = AccessTools.TypeByName("PlanBuild.Blueprints.PieceEntry");
                var existing = PlanBuildAdapter.Blueprints().Single(b => (string)PlanBuildAdapter.Field(b, "Name") == "Rune native test");
                object blueprint = AccessTools.Method(type, "FromArray").Invoke(null, new object[] { "rune-staging-test", new[] { "#Name:Rune staging test", "#Pieces", "piece_workbench;Crafting;0;0;0;0;0;0;1;;1;1;1" }, Enum.Parse(type.GetNestedType("Format"), "Blueprint") });
                var entries = Array.CreateInstance(entryType, 2); var inverse = Quaternion.Inverse(Quaternion.Euler(0, npc.transform.eulerAngles.y, 0));
                for (int i = 0; i < 2; i++) entries.SetValue(Activator.CreateInstance(entryType, new object[] { "piece_workbench", "Crafting", inverse * ((i == 0 ? first : second) - npc.transform.position), inverse, "", Vector3.one }), i);
                AccessTools.Field(type, "PieceEntries").SetValue(blueprint, entries);
                ((IDictionary)AccessTools.Field(AccessTools.TypeByName("PlanBuild.Blueprints.BlueprintManager"), "LocalBlueprints").GetValue(null))["rune-staging-test"] = blueprint;
                before = UnityEngine.Object.FindObjectsOfType<Piece>().Count(p => p.name.StartsWith("piece_workbench") && !p.GetComponent(PlanBuildAdapter.PlanType) && p.GetCreator() == player.GetPlayerID());
                npc.Body.GetInventory().AddItem(ZNetScene.instance.GetPrefab("Wood"), 5); // 10 kept from cancelled job + 5.
                var drop = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Wood"), first + Vector3.up * .3f, Quaternion.identity).GetComponent<ItemDrop>();
                drop.m_itemData.m_dropPrefab = ZNetScene.instance.GetPrefab("Wood"); drop.m_itemData.m_stack = 5;
                drop.GetComponent<Rigidbody>().constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
                AccessTools.Method(typeof(ItemDrop), "Save").Invoke(drop, null);
                string response = npc.Order("planbuild_self", 1, "Rune staging test");
                if (!npc.LastOrderAccepted || npc.QuestSnapshot().materials.Single().required != 20) throw new Exception("Named two-piece job rejected/wrong checklist: " + response);
                report.Add("Started named multi-piece blueprint: " + response);
                player.TeleportTo(first + Vector3.up * .2f, Quaternion.identity, true); AccessTools.Field(typeof(Companion), "craftAt").SetValue(npc, Time.time + 15); running = true;
            } catch (Exception e) { report.Add("FAILED multi-piece setup: " + e); }
            float until = Time.realtimeSinceStartup + 150, logAt = 0;
            while (running && npc.QuestSnapshot()?.status == "Active" && Time.realtimeSinceStartup < until) {
                player.SetHealth(player.GetMaxHealth());
                foreach (var hostile in Character.GetAllCharacters().Where(c => c && BaseAI.IsEnemy(npc.Body, c) && Vector3.Distance(c.transform.position, first) < 80).ToArray()) ZNetScene.instance.Destroy(hostile.gameObject);
                if (Time.realtimeSinceStartup >= logAt) { logAt = Time.realtimeSinceStartup + 5; report.Add("Multi-piece: " + npc.TaskLabel + "; wood=" + npc.Count("Wood")); File.WriteAllLines(output, report); }
                yield return new WaitForSecondsRealtime(.25f);
            }
            try {
                if (!running || npc.QuestSnapshot()?.status != "Complete") throw new Exception("Multi-piece blueprint did not complete: " + npc.QuestSnapshot()?.note);
                var built = UnityEngine.Object.FindObjectsOfType<Piece>().Where(p => p.name.StartsWith("piece_workbench") && !p.GetComponent(PlanBuildAdapter.PlanType) && p.GetCreator() == player.GetPlayerID()).ToArray();
                if (built.Length != before + 2 || !built.Any(p => Vector3.Distance(p.transform.position, first) < .1f) || !built.Any(p => Vector3.Distance(p.transform.position, second) < .1f) || npc.Count("Wood") != 0) throw new Exception("Multi-piece result or material conservation mismatch");
                report.Add("PASS: Named planbuild_self placed and completed two native PlanBuild pieces at transformed blueprint positions; gathered the missing five wood, consumed exactly twenty, and completed the checklist.");
            } catch (Exception e) { report.Add("FAILED multi-piece behavior: " + e); }
            File.WriteAllLines(output, report);
        }
    }
}


