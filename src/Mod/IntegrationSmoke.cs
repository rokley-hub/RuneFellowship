using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Rune.Mod
{
    // Only runs with an explicit environment flag AND a separate supplied save directory.
    // Never entered during normal play. No existing world or profile is opened.
    internal static class IntegrationSmoke
    {
        public static IEnumerator Run(Plugin plugin)
        {
            string output = Environment.GetEnvironmentVariable("RUNE_INTEGRATION_SMOKE");
            string saveRoot = Environment.GetEnvironmentVariable("RUNE_SMOKE_SAVE_ROOT");
            var args = Environment.GetCommandLineArgs(); int saveIndex = Array.IndexOf(args, "-savedir");
            if (string.IsNullOrEmpty(saveRoot) || saveIndex < 0 || saveIndex + 1 >= args.Length || Path.GetFullPath(args[saveIndex + 1]) != Path.GetFullPath(saveRoot)) { File.WriteAllText(output, "Refused: isolated save folder missing."); yield break; }
            // The client only applies -savedir for dedicated-server startup. Set and verify
            // the actual runtime path before creating any synthetic profile or world.
            Utils.SetSaveDataPath(saveRoot);
            yield return new WaitForSecondsRealtime(12);
            string expectedRoot = Path.GetFullPath(saveRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(GameCompatibility.WorldSavePath()).StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFullPath(GameCompatibility.CharacterSavePath()).StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)) {
                File.WriteAllText(output, "Refused: game save paths are not isolated."); Application.Quit(); yield break;
            }
            var report = new List<string>();
            try {
                var profile = new PlayerProfile("rune-smoke-02", FileHelpers.FileSource.Local); profile.SetName("RuneSmoke"); profile.Save();
                Game.SetProfile("rune-smoke-02", FileHelpers.FileSource.Local);
                var world = new World("RuneSmoke02", "RuneChecks02") { m_fileSource = FileHelpers.FileSource.Local };
                ZNet.SetServer(true, false, false, "", "RuneSmoke02", world); ZNet.ResetServerHost();
                var startup = UnityEngine.Object.FindObjectOfType<FejdStartup>();
                AccessTools.Field(typeof(FejdStartup), "m_startingWorld").SetValue(startup, true);
                AccessTools.Method(typeof(FejdStartup), "LoadMainScene").Invoke(startup, null);
            } catch (Exception e) { report.Add("SETUP FAILED: " + e); File.WriteAllLines(output, report); Application.Quit(); yield break; }
            float end = Time.realtimeSinceStartup + 180;
            while ((!Player.m_localPlayer || !ZNetScene.instance) && Time.realtimeSinceStartup < end) yield return new WaitForSecondsRealtime(1);
            yield return new WaitForSecondsRealtime(10);
            if (!Player.m_localPlayer) { File.WriteAllText(output, "FAILED: test player did not load."); Application.Quit(); yield break; }
            var player = Player.m_localPlayer;
            player.AttachStop(); player.SetIntro(false); player.SetGodMode(true);
            foreach (var bird in UnityEngine.Object.FindObjectsOfType<Valkyrie>()) UnityEngine.Object.Destroy(bird.gameObject);
            report.Add("Test world loaded; solo=" + Plugin.Solo);
            Vector3 center = player.transform.position;
            Heightmap.GetHeight(center, out float ground); center.y = ground + 1;
            player.transform.position = center;
            if (Environment.GetEnvironmentVariable("RUNE_ROSTER_SMOKE") == "1") { yield return RosterSmoke.Run(plugin,player,output); yield break; }
            if (Environment.GetEnvironmentVariable("RUNE_RELIABILITY_SMOKE") == "1") {
                yield return ReliabilitySmoke.Run(plugin, player, output + ".reliability.txt");
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RUNE_BLUEPRINT_LIBRARY_SMOKE"))) { Application.Quit(); yield break; }
            }
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RUNE_BLUEPRINT_LIBRARY_SMOKE"))) { yield return BlueprintLibrarySmoke.Run(plugin, player, output); yield break; }
            if (Environment.GetEnvironmentVariable("RUNE_WORKBENCH_SMOKE") == "1") { yield return WorkbenchSmoke.Run(plugin, player, output); yield break; }
            if (Environment.GetEnvironmentVariable("RUNE_HEALTH_PRIORITY_SMOKE") == "1") { yield return HealthPrioritySmoke.Run(plugin, player, output); yield break; }
            if (Environment.GetEnvironmentVariable("RUNE_DIAGNOSTICS_SMOKE") == "1") { yield return DiagnosticsSmoke.Run(plugin, player, output); yield break; }
            if (Environment.GetEnvironmentVariable("RUNE_EQUIPMENT_CARE_SMOKE") == "1") { yield return EquipmentCareSmoke.Run(plugin, player, output); Application.Quit(); yield break; }
            if (Environment.GetEnvironmentVariable("RUNE_PICKUP_SMOKE") == "1") { yield return PickupRecallSmoke.Run(plugin, player, output); Application.Quit(); yield break; }
            if (Environment.GetEnvironmentVariable("RUNE_SUPPLIES_SMOKE") == "1") { yield return SuppliesSmoke.Run(plugin, player, output); yield return EquipmentCareSmoke.Run(plugin, player, output + ".equipment.txt"); Application.Quit(); yield break; }
            if (Environment.GetEnvironmentVariable("RUNE_COMBAT_SMOKE") == "1") { yield return CombatSmoke.Run(plugin, player, output); Application.Quit(); yield break; }
            if (Environment.GetEnvironmentVariable("RUNE_ORDERS_SMOKE") == "1") { yield return OrdersSmoke.Run(plugin, player, output); Application.Quit(); yield break; }
            var spawned = new List<Companion>();
            foreach (var skin in new[] { "skeleton", "draugr", "elite", "dwarf", "wolf" }) {
                Companion companion = null;
                try {
                    var prefab = ZNetScene.instance.GetPrefab(Plugin.PrefabFor(skin));
                    var npc = UnityEngine.Object.Instantiate(prefab, center + Vector3.right * (spawned.Count * 3 + 3), Quaternion.identity);
                    companion = npc.GetComponent<Companion>(); companion.Bind(player, skin == "dwarf" ? "eira" : skin == "wolf" ? "bjorn" : "rune", skin);
                } catch (Exception e) { report.Add(skin + " SPAWN FAILED: " + e); }
                yield return new WaitForSecondsRealtime(2);
                try {
                    if (!companion) continue;
                    report.Add(skin + ": ready=" + companion.Ready + "; health=" + companion.Body.GetMaxHealth() + "; inventory=" + companion.Body.GetInventory().NrOfItems() + "; playerComponent=" + (bool)companion.GetComponent<Player>());
                    companion.Order("stay", 20);
                    if (skin == "dwarf") {
                        var armour = ZNetScene.instance.GetPrefab("ArmorIronChest").GetComponent<ItemDrop>().m_itemData.Clone(); armour.m_dropPrefab = ZNetScene.instance.GetPrefab("ArmorIronChest");
                        companion.Body.GetInventory().AddItem(armour); bool equipped = companion.Body.EquipItem(armour, false);
                        report.Add("Dwarf iron chest: equipped=" + equipped + "; armor=" + companion.Body.GetBodyArmor());
                        companion.UpdateIdentity("Astrid", "female", "Balanced", false);
                        report.Add("Female dwarf model=" + companion.GetComponent<VisEquipment>().GetModelIndex() + "; name=" + companion.DisplayName + "; avoidsBosses=" + !companion.JoinBossFights);
                    }
                    report.Add(skin + " exclusion: " + companion.Order("exclude_item", 20, "resin"));
                    spawned.Add(companion);
                } catch (Exception e) { report.Add(skin + " CHECK FAILED: " + e); }
            }
            int beforeCargo = -1; float beforeHealth = -1; bool bodyChangeStarted = false;
            try {
                var dwarf = Plugin.Find("eira"); beforeCargo = dwarf.CargoCount; beforeHealth = dwarf.Body.GetHealth();
                var changed = plugin.Execute(new Rune.Shared.Command { id = Guid.NewGuid().ToString(), world = Plugin.World, timestamp = Rune.Shared.Rules.Now, action = "update_profile", companionId = "eira", appearance = "wolf", displayName = "Astrid", gender = "female", combatStyle = "Balanced", joinBossFights = false });
                bodyChangeStarted = changed.accepted;
                report.Add("Body change accepted=" + changed.accepted + "; message=" + changed.message);
            } catch (Exception e) { report.Add("BODY CHANGE FAILED: " + e); }
            if (bodyChangeStarted) {
                yield return new WaitForSecondsRealtime(3);
                try {
                    var replacement = Plugin.Find("eira"); report.Add("Body replacement: ready=" + (replacement && replacement.Ready) + "; appearance=" + (replacement ? replacement.Appearance : "missing") + "; cargo=" + (replacement ? replacement.CargoCount : -1) + "/" + beforeCargo + "; health=" + (replacement ? replacement.Body.GetHealth() : -1) + "/" + beforeHealth);
                } catch (Exception e) { report.Add("BODY REPLACEMENT CHECK FAILED: " + e); }
            }
            // Inventory and recipe integration checks use items created only in this test world.
            try {
                var npc = spawned.Find(c => c.Appearance == "skeleton");
                npc.Order("stay", 20);
                var wood = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Wood"), npc.transform.position + Vector3.forward, Quaternion.identity).GetComponent<ItemDrop>(); wood.m_itemData.m_stack = 7;
                AccessTools.Method(typeof(Companion), "TakeDrop").Invoke(npc, new object[] { wood });
                report.Add("Real dropped wood transferred: " + npc.Count("Wood") + "; worldDropDestroyed=" + (!wood));
                var resin = ZNetScene.instance.GetPrefab("Resin").GetComponent<ItemDrop>().m_itemData;
                report.Add("Resin excluded=" + AccessTools.Method(typeof(Companion), "ExcludedItem").Invoke(npc, new object[] { resin }));
                var inv = npc.Body.GetInventory(); var before = inv.NrOfItems(); var saved = new ZPackage(); inv.Save(saved);
                var restored = new Inventory("smoke", null, 8, 4); restored.Load(new ZPackage(saved.GetArray()));
                report.Add("Inventory persistence: before=" + before + "; restored=" + restored.NrOfItems());
                player.transform.position = center; npc.Order("set_base", 20);
                var chestObject = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("piece_chest_wood"), center + Vector3.forward * 3, Quaternion.identity); chestObject.GetComponent<Piece>().AssignCreator(player.GetPlayerID());
                var chest = chestObject.GetComponent<Container>(); var item = inv.GetAllItems().Find(i => i.m_dropPrefab && i.m_dropPrefab.name == "Wood");
                report.Add("Chest access=" + AccessTools.Method(typeof(Companion), "CanChest").Invoke(npc, new object[] { chest }) + "; baseDistance=" + Vector3.Distance(center, player.transform.position));
                bool moved = (bool)AccessTools.Method(typeof(Companion), "Transfer").Invoke(npc, new object[] { inv, chest.GetInventory(), item, 3, null, chest });
                report.Add("Storage transfer=" + moved + "; wood total=" + (npc.Count("Wood") + chest.GetInventory().CountItems(item.m_shared.m_name)));
                var recipe = ObjectDB.instance.m_recipes.Find(r => r && r.m_item && r.m_item.name == "AxeStone");
                AccessTools.Method(typeof(Player), "AddKnownRecipe").Invoke(player, new object[] { recipe });
                foreach (var requirement in recipe.m_resources) if (requirement.m_resItem) inv.AddItem(requirement.m_resItem.gameObject, requirement.GetAmount(1));
                int axes = npc.Count("AxeStone");
                report.Add("Craft order: " + npc.Order("craft_item", 20, "AxeStone"));
                var quest = npc.QuestSnapshot();
                if (quest == null || quest.status != "Active" || quest.materials.Length == 0 || quest.materials.Any(n => n.missing != 0)) throw new Exception("Crafting quest did not reflect supplied ingredients.");
                report.Add("Crafting checklist uses live recipe requirements and supplied cargo: passed");
                AccessTools.Method(typeof(Companion), "TickCraftPlan").Invoke(npc, new object[] { .1f });
                report.Add("Crafted axe count: before=" + axes + "; after=" + npc.Count("AxeStone"));
                if (npc.Count("AxeStone") <= axes || npc.QuestSnapshot().status != "Complete") throw new Exception("Craft completion did not update the checklist.");
                npc.Order("craft_item", 20, "AxeStone");
                int cargoBeforeClear = npc.CargoCount;
                report.Add(npc.Order("clear_crafting", 20));
                if (npc.QuestSnapshot() != null || npc.CargoCount != cargoBeforeClear || AccessTools.Field(typeof(Companion), "craftPlan").GetValue(npc) != null) throw new Exception("Clear crafting failed to clear safely.");
                report.Add("Clear crafting stops the job, empties the checklist and preserves cargo: passed");
                var bronzeRecipe = ObjectDB.instance.m_recipes.First(r => r && r.m_item && r.m_item.name == "Bronze");
                var nailRecipe = ObjectDB.instance.m_recipes.First(r => r && r.m_item && r.m_item.name.IndexOf("Bronze", StringComparison.OrdinalIgnoreCase) >= 0 && r.m_item.name.IndexOf("Nail", StringComparison.OrdinalIgnoreCase) >= 0);
                AccessTools.Method(typeof(Player), "AddKnownRecipe").Invoke(player, new object[] { bronzeRecipe });
                AccessTools.Method(typeof(Player), "AddKnownRecipe").Invoke(player, new object[] { nailRecipe });
                npc.Order("craft_item", 20, nailRecipe.m_item.name);
                var nested = npc.QuestSnapshot();
                if (nested == null || !bronzeRecipe.m_resources.Where(r => r.m_resItem).All(r => nested.gather.Any(n => n.prefab == r.m_resItem.name && n.missing > 0))) throw new Exception("Intermediate bronze recipe did not expand into its inputs.");
                report.Add("Craftable intermediate ingredients expand into copper and tin: passed");
                npc.Order("stay", 20);
                AccessTools.Method(typeof(Companion), "LoadCraftingQuest").Invoke(npc, null);
                if (npc.QuestSnapshot() == null || npc.QuestSnapshot().status != "Paused") throw new Exception("Saved checklist was not retained as paused.");
                report.Add("Crafting checklist persists and restores as paused: passed");
                npc.Order("clear_crafting", 20);
                AccessTools.Field(typeof(Plugin), "recipeCatalogAt").SetValue(plugin, 0f);
                AccessTools.Method(typeof(Plugin), "WriteRecipeCatalog").Invoke(plugin, null);
                var book = WireJson.Read<Rune.Shared.RecipeBook>(File.ReadAllText(Path.Combine(Plugin.BridgePath, "recipes.json")));
                if (!book.recipes.Any(r => r.prefab == "AxeStone" && r.known && r.materials.Length > 0)) throw new Exception("Recipe catalog missing learned stone axe.");
                report.Add("Live recipe catalog: " + book.recipes.Length + " entries; learned recipe and ingredient data passed");
                npc.Order("stay", 20);
            } catch (Exception e) { report.Add("ITEM CHECK FAILED: " + e); }
            if (Environment.GetEnvironmentVariable("RUNE_CRAFTING_SMOKE") == "1") {
                try {
                    var npc = spawned.Find(c => c && c.Appearance == "skeleton"); npc.Order("craft_item", 20, "AxeStone");
                    AccessTools.Method(typeof(Companion), "BlockCraft").Invoke(npc, new object[] { "Test: supply the missing materials, then ask to craft again." });
                    AccessTools.Field(typeof(Plugin), "show").SetValue(plugin, true); Jotunn.Managers.GUIManager.BlockInput(true);
                } catch (Exception e) { report.Add("OVERLAY PREVIEW FAILED: " + e); }
                yield return new WaitForSecondsRealtime(1);
                ScreenCapture.CaptureScreenshot(Path.ChangeExtension(output, ".png"));
                yield return new WaitForSecondsRealtime(2);
                File.WriteAllLines(output, report); Application.Quit(); yield break;
            }
            // Regression: non-convex collider distances must not collapse to the caller position.
            GameObject meshObject = null;
            try {
                meshObject = new GameObject("RuneDistanceRegression"); meshObject.transform.position = center + Vector3.right * 10;
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var mesh = meshObject.AddComponent<MeshCollider>(); mesh.sharedMesh = cube.GetComponent<MeshFilter>().sharedMesh; mesh.convex = false;
                UnityEngine.Object.Destroy(cube);
                var resource = new ResourceTarget { Component = mesh, Collider = mesh };
                float far = Vector3.Distance(center, resource.Approach(center));
                report.Add("Nonconvex resource distance=" + far + "; distantResourceNotInReach=" + (far > 8));
                string state = WireJson.Write(new Rune.Shared.GameState { roster = new[] { new Rune.Shared.CompanionState { id = "eira" } } });
                report.Add("Roster serializes=" + state.Contains("roster"));
            } catch (Exception e) { report.Add("DISTANCE CHECK FAILED: " + e); }
            if (meshObject) UnityEngine.Object.Destroy(meshObject);
            // Force a real tree target, but let normal game ticks move and harvest it.
            Companion worker = spawned.Find(c => c.Appearance == "skeleton");
            TreeBase testTree = null; Vector3 workerStart = Vector3.zero; bool harvestSetup = false;
            try {
                Vector3 start = center + Vector3.right * 27; Heightmap.GetHeight(start, out float h); start.y = h + .3f;
                player.transform.position = start; worker.transform.position = start;
                Vector3 treePoint = start + Vector3.forward * 9; Heightmap.GetHeight(treePoint, out h); treePoint.y = h;
                testTree = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Beech1"), treePoint, Quaternion.identity).GetComponent<TreeBase>();
                workerStart = start; harvestSetup = true;
            } catch (Exception e) { report.Add("HARVEST SETUP FAILED: " + e); }
            yield return new WaitForSecondsRealtime(2);
            if (harvestSetup) {
                Collider chosen = null;
                try {
                    foreach (var col in testTree.GetComponentsInChildren<Collider>()) if (col && col.enabled && !col.isTrigger) { chosen = col; if (col is MeshCollider) break; }
                    worker.Order("gather_wood", 5);
                    AccessTools.Field(typeof(Companion), "target").SetValue(worker, new ResourceTarget { Component = testTree, Collider = chosen, ToolTier = testTree.m_minToolTier });
                    AccessTools.Field(typeof(Companion), "lastProgress").SetValue(worker, Time.time);
                    AccessTools.Field(typeof(Companion), "closestDistance").SetValue(worker, float.MaxValue);
                } catch (Exception e) { report.Add("HARVEST ORDER FAILED: " + e); }
                float until = Time.realtimeSinceStartup + 35; float moved = 0; bool swung = false; float hitDistance = -1;
                while (Time.realtimeSinceStartup < until) {
                    yield return new WaitForSecondsRealtime(.25f);
                    moved = Mathf.Max(moved, Vector3.Distance(workerStart, worker.transform.position));
                    float attackAt = (float)AccessTools.Field(typeof(Companion), "lastAttack").GetValue(worker);
                    if (attackAt > 0 && !swung) { swung = true; hitDistance = chosen ? Vector3.Distance(worker.transform.position, new ResourceTarget { Component = testTree, Collider = chosen }.Approach(worker.transform.position)) : -1; }
                    if (swung && moved > 3) break;
                }
                report.Add("Tree job movement=" + moved + "; swing=" + swung + "; hitDistance=" + hitDistance + "; movementBeforeChopPassed=" + (moved > 3 && swung && hitDistance >= 0 && hitDistance <= 1.8f));
                worker.Order("stay", 20);
            }
            yield return new WaitForSecondsRealtime(4);
            File.WriteAllLines(output, report);
            Application.Quit();
        }
    }
}



