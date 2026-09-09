using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Rune.Mod
{
    // Entered only after IntegrationSmoke verifies the isolated save directories.
    internal static class BlueprintLibrarySmoke
    {
        private static void PositionFixturePlayer(Player player, Vector3 point)
        {
            // Fixture setup only; synchronize both physics and transform before
            // freezing a request reference. Production companions never teleport.
            player.transform.position = point;
            var body = player.GetComponent<Rigidbody>();
            if (body) { body.position = point; body.velocity = Vector3.zero; }
        }
        internal static IEnumerator Run(Plugin plugin, Player player, string output)
        {
            var report = new List<string>();
            var library = Environment.GetEnvironmentVariable("RUNE_BLUEPRINT_LIBRARY_SMOKE");
            var type = AccessTools.TypeByName("PlanBuild.Blueprints.Blueprint");
            var blueprints = (IDictionary)AccessTools.Field(AccessTools.TypeByName("PlanBuild.Blueprints.BlueprintManager"), "LocalBlueprints").GetValue(null);
            Companion npc = null;
            Vector3 site = player.transform.position + Vector3.right * 25;
            Heightmap.GetHeight(site, out float ground); site.y = ground;
            // Use PlanBuild's native terrain tool for the isolated prepared test plot only.
            var terrain = AccessTools.TypeByName("PlanBuild.Blueprints.TerrainTools");
            report.Add("Terrain methods: " + string.Join("; ", terrain.GetMethods().Where(m => m.Name.Contains("Level") || m.Name.Contains("Flatten")).Select(m => m.ToString())));
            File.WriteAllLines(output, report);
            try {
                // The test terrain is a prerequisite, not a feature added to the blueprint.
                var block = AccessTools.TypeByName("PlanBuild.Blueprints.BlockCheck");
                object indices = AccessTools.Method(terrain, "GetCompilerIndicesWithCircle").Invoke(null, new object[] { site + Vector3.forward * 2, 26f, Enum.Parse(block, "Off") });
                AccessTools.Method(terrain, "LevelTerrain").Invoke(null, new object[] { indices, site + Vector3.forward * 2, 13f, 0f, site.y });
                foreach (var collider in Physics.OverlapSphere(site, 13, LayerMask.GetMask("piece", "static_solid", "Default"))) {
                    if (!collider || collider.GetComponentInParent<Heightmap>() || collider.GetComponentInParent<Character>()) continue;
                    var view = collider.GetComponentInParent<ZNetView>();
                    if (view && view.IsValid()) ZNetScene.instance.Destroy(view.gameObject);
                }
                report.Add("Prepared isolated level test ground at " + site);
            } catch (Exception e) { report.Add("FAILED terrain fixture: " + e); File.WriteAllLines(output, report); Application.Quit(); yield break; }
            yield return new WaitForSecondsRealtime(5);
            var collisionBench = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("piece_workbench"), site + new Vector3(7, 0, -5), Quaternion.identity);
            var collisionWall = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("woodwall"), site + new Vector3(8, 1, 0), Quaternion.identity);
            yield return new WaitForSecondsRealtime(1);
            bool collisionPassed = false;
            try {
                var check = AccessTools.Method(typeof(Companion), "ConstructionSpace");
                var benchPrefab = ZNetScene.instance.GetPrefab("piece_workbench").GetComponent<Piece>();
                if ((bool)check.Invoke(null, new object[] { benchPrefab, collisionBench.transform.position, Quaternion.identity, null })) throw new Exception("Occupied bench site accepted");
                if (!(bool)check.Invoke(null, new object[] { benchPrefab, site, Quaternion.identity, null })) throw new Exception("Clear bench site rejected");
                if (!(bool)check.Invoke(null, new object[] { ZNetScene.instance.GetPrefab("woodwall").GetComponent<Piece>(), site + new Vector3(9, 1, 1), Quaternion.Euler(0, 90, 0), null })) throw new Exception("Normal perpendicular wall joint rejected");
                report.Add("PASS native placement: occupied bench rejected, clear bench accepted, shallow snapped wall joint accepted."); collisionPassed = true;
            } catch (Exception e) { report.Add("FAILED placement query: " + e); }
            ZNetScene.instance.Destroy(collisionBench); ZNetScene.instance.Destroy(collisionWall);
            File.WriteAllLines(output, report);
            if (!collisionPassed) { Application.Quit(); yield break; }
            string filter = Environment.GetEnvironmentVariable("RUNE_BLUEPRINT_FILTER") ?? "";
            foreach (var file in Directory.GetFiles(library, "*.blueprint").Where(f => Path.GetFileName(f).Contains(filter)).OrderBy(f => f)) {
                bool started = false; string name = ""; List<PlanBuildAdapter.Entry> entries = null;
                Container supplies = null; var fixtures = new HashSet<int>();
                var expected = new Dictionary<string, int>();
                try {
                    foreach (var p in UnityEngine.Object.FindObjectsOfType<Piece>().Where(p => p && p.GetCreator() == player.GetPlayerID()).ToArray()) ZNetScene.instance.Destroy(p.gameObject);
                    if (npc) ZNetScene.instance.Destroy(npc.gameObject);
                    foreach (var drop in UnityEngine.Object.FindObjectsOfType<ItemDrop>().Where(d => d && Vector3.Distance(d.transform.position, site) < 30).ToArray()) ZNetScene.instance.Destroy(drop.gameObject);
                    object blueprint = blueprints[Path.GetFileNameWithoutExtension(file)];
                    if (blueprint == null) throw new Exception("PlanBuild did not load the library file on startup: " + file);
                    name = (string)PlanBuildAdapter.Field(blueprint, "Name");
                    entries = PlanBuildAdapter.Resolve(name, site, Quaternion.identity);
                    foreach (var e in entries) {
                        var piece = PlanBuildAdapter.Original(e.Prefab.GetComponent(PlanBuildAdapter.PlanType));
                        if (piece.name.StartsWith("stone_wall")) report.Add("Native stone bounds: " + piece.name + " " + string.Join(";", piece.GetComponentsInChildren<BoxCollider>().Select(c => "centre=" + piece.transform.InverseTransformPoint(c.transform.TransformPoint(c.center)) + " size=" + Vector3.Scale(c.size, c.transform.lossyScale))));
                        if (piece.m_craftingStation) AccessTools.Method(typeof(Player), "AddKnownStation").Invoke(player, new object[] { piece.m_craftingStation });
                        foreach (var req in piece.m_resources.Where(r => r.m_resItem)) {
                            string id = req.m_resItem.name; expected[id] = (expected.TryGetValue(id, out int n) ? n : 0) + req.GetAmount(1);
                            AccessTools.Method(typeof(Player), "AddKnownItem").Invoke(player, new object[] { req.m_resItem.m_itemData });
                        }
                    }
                    foreach (var id in new[] { "piece_chest_wood", "piece_stonecutter" }) {
                        if (id == "piece_stonecutter" && name.StartsWith("Camp")) continue;
                        var fixture = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(id), site + new Vector3(-7, 0, id.Contains("chest") ? -3 : 3), Quaternion.identity).GetComponent<Piece>();
                        fixture.SetCreator(player.GetPlayerID()); fixtures.Add(fixture.GetInstanceID());
                        if (id.Contains("chest")) supplies = fixture.GetComponent<Container>();
                    }
                    report.Add(name + " parsed: " + entries.Count + " pieces; materials=" + string.Join(",", expected.Select(k => k.Key + ":" + k.Value)));
                    var go = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(Plugin.PrefabFor("dwarf")), site + Vector3.back * 3 + Vector3.up * .3f, Quaternion.identity);
                    npc = go.GetComponent<Companion>(); npc.Bind(player, "library-test", "dwarf", "Library test", "female", "Balanced", true);
                    PositionFixturePlayer(player, site + Vector3.back * 8 + Vector3.up);
                } catch (Exception e) { report.Add("FAILED setup " + name + ": " + e); }
                File.WriteAllLines(output, report);
                yield return new WaitForSecondsRealtime(8);
                try {
                    PositionFixturePlayer(player, site + Vector3.back * 8 + Vector3.up);
                    report.Add("Fixture reference=" + player.transform.position + ", time scale=" + Time.timeScale);
                    if (npc == null || !npc.Ready) throw new Exception("Companion did not initialize");
                    npc.Body.GetInventory().RemoveAll(); npc.Body.GetInventory().AddItem(ZNetScene.instance.GetPrefab("Hammer"), 1);
                    supplies.GetInventory().RemoveAll();
                    foreach (var req in expected) {
                        var prefab = ZNetScene.instance.GetPrefab(req.Key);
                        int remaining = req.Value, stack = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize;
                        while (remaining > 0) { int amount = Math.Min(stack, remaining); if (!supplies.GetInventory().AddItem(prefab, amount)) throw new Exception("Fixture chest cannot hold " + req.Key); remaining -= amount; }
                    }
                    npc.Order("set_base", 1);
                    // Reproduce missing notification-cache entries, without unlocking pieces.
                    var known = (HashSet<string>)AccessTools.Field(typeof(Player), "m_knownRecipes").GetValue(player);
                    var benchPiece = ZNetScene.instance.GetPrefab("piece_workbench").GetComponent<Piece>();
                    known.Remove(benchPiece.m_name);
                    var materials = (HashSet<string>)AccessTools.Field(typeof(Player), "m_knownMaterial").GetValue(player);
                    var woodName = ZNetScene.instance.GetPrefab("Wood").GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
                    materials.Remove(woodName);
                    if (BuildingKnowledge.Known(player, benchPiece)) throw new Exception("Undiscovered wood incorrectly unlocked bench");
                    materials.Add(woodName);
                    foreach (var e in entries) {
                        var piece = PlanBuildAdapter.Original(e.Prefab.GetComponent(PlanBuildAdapter.PlanType));
                        known.Remove(piece.m_name);
                        if (player.IsRecipeKnown(piece.m_name) || !BuildingKnowledge.Known(player, piece)) throw new Exception("Native discovery fallback failed: " + piece.name);
                    }
                    report.Add("PASS discovery: absent recipe-cache entries accepted from native learned materials/stations, without AddKnownPiece.");
                    // Pin only the reference for acceptance; normal movement resumes immediately.
                    npc.transform.position = site; npc.transform.rotation = Quaternion.identity;
                    // One whole design uses independently placed plans, the other two saved names.
                    if (name.StartsWith("Camp")) foreach (var e in entries) PlanBuildAdapter.Place(e, player.GetPlayerID());
                    string response = npc.Order(name.StartsWith("Camp") ? "finish_plan" : "planbuild_self", 1, name.Replace(" ", "_"));
                    report.Add(name + " accepted=" + npc.LastOrderAccepted + ": " + response);
                    if (npc.LastOrderAccepted) {
                        int count = UnityEngine.Object.FindObjectsOfType(PlanBuildAdapter.PlanType).Length;
                        var originalCraft = AccessTools.Field(typeof(Companion), "craftPlan").GetValue(npc);
                        var originalJob = PlanBuildAdapter.Field(originalCraft, "Construction");
                        var originalOrigin = (Vector3)PlanBuildAdapter.Field(originalJob, "Origin");
                        npc.Order("follow", 1);
                        AccessTools.Field(typeof(Companion), "suspendedJob").SetValue(npc, null);
                        AccessTools.Method(typeof(Companion), "LoadJob").Invoke(npc, null);
                        npc.transform.position = site + Vector3.right * 3;
                        string resumed = npc.Order("resume_task", 1);
                        if (!npc.LastOrderAccepted) throw new Exception("Resume rejected: " + resumed);
                        var craft = AccessTools.Field(typeof(Companion), "craftPlan").GetValue(npc);
                        var job = PlanBuildAdapter.Field(craft, "Construction");
                        if (Vector3.Distance((Vector3)PlanBuildAdapter.Field(job, "Origin"), originalOrigin) > .01f || UnityEngine.Object.FindObjectsOfType(PlanBuildAdapter.PlanType).Length != count)
                            throw new Exception("Resumption moved the original reference or duplicated plans");
                        report.Add("PASS resume: restored from native saved job, retained original anchor and reused every existing plan.");
                    }
                    started = npc.LastOrderAccepted;
                } catch (Exception e) { report.Add("FAILED order " + name + ": " + e); }
                float until = Time.realtimeSinceStartup + 420, logAt = 0;
                while (started && npc.QuestSnapshot()?.status == "Active" && Time.realtimeSinceStartup < until) {
                    player.SetHealth(player.GetMaxHealth()); npc.Body.SetHealth(npc.Body.GetMaxHealth());
                    foreach (var hostile in Character.GetAllCharacters().Where(c => c && BaseAI.IsEnemy(npc.Body, c) && Vector3.Distance(c.transform.position, site) < 80).ToArray()) ZNetScene.instance.Destroy(hostile.gameObject);
                    if (Time.realtimeSinceStartup > logAt) {
                        logAt = Time.realtimeSinceStartup + 5;
                        object craft = AccessTools.Field(typeof(Companion), "craftPlan").GetValue(npc);
                        object job = craft == null ? null : PlanBuildAdapter.Field(craft, "Construction");
                        var targetPlan = job == null ? null : PlanBuildAdapter.Field(job, "Target") as Component;
                        string route = job == null ? "" : " stand=" + PlanBuildAdapter.Field(job, "StandingPoint") + " hasStand=" + PlanBuildAdapter.Field(job, "HasStandingPoint") + " target=" + (targetPlan ? PlanBuildAdapter.Original(targetPlan).name + " at " + targetPlan.transform.position : "none");
                        report.Add(name + ": " + npc.TaskLabel + " pos=" + npc.transform.position + route); File.WriteAllLines(output, report);
                    }
                    yield return new WaitForSecondsRealtime(.25f);
                }
                yield return new WaitForSecondsRealtime(8);
                Capture(site, Path.Combine(Path.GetDirectoryName(output), name.Replace(" ", "-") + ".png"));
                try {
                    if (!started || npc.QuestSnapshot()?.status != "Complete") throw new Exception("Did not finish: " + npc.QuestSnapshot()?.note);
                    var built = UnityEngine.Object.FindObjectsOfType<Piece>().Where(p => p && p.GetCreator() == player.GetPlayerID() && !fixtures.Contains(p.GetInstanceID()) && !p.GetComponent(PlanBuildAdapter.PlanType)).ToArray();
                    if (built.Length != entries.Count) throw new Exception("Expected " + entries.Count + " real pieces; got " + built.Length);
                    if (expected.Any(k => npc.Count(k.Key) != 0)) throw new Exception("Unspent material unexpectedly remains");
                    if (supplies.GetInventory().NrOfItems() != 0) throw new Exception("Supply chest ingredients remain");
                    foreach (var station in built.Select(p => p.GetComponent<CraftingStation>()).Where(s => s)) {
                        if (!station.CheckUsable(player, false)) throw new Exception("Station not usable: " + station.name);
                        report.Add("PASS usable native station: " + station.name);
                    }
                    var bed = built.Select(p => p.GetComponent<Bed>()).Single(b => b);
                    if (!(bool)AccessTools.Method(typeof(Bed), "CheckExposure").Invoke(bed, new object[] { player })) throw new Exception("Bed lacks native shelter/cover");
                    if (!(bool)AccessTools.Method(typeof(Bed), "CheckFire").Invoke(bed, new object[] { player })) throw new Exception("Exterior fire does not warm bed");
                    // Human-sized clearance through the entrance and along the central aisle.
                    for (float z = -1; z <= 5.4f; z += .2f) {
                        var foot = site + new Vector3(0, .15f, z);
                        var hits = Physics.OverlapCapsule(foot + Vector3.up * .35f, foot + Vector3.up * 1.45f, .35f, LayerMask.GetMask("piece", "static_solid", "Default"), QueryTriggerInteraction.Ignore);
                        if (hits.Any(c => c && !c.GetComponentInParent<Heightmap>() && !c.GetComponentInParent<Character>())) throw new Exception("Blocked human walkway at z=" + z + ": " + string.Join(",", hits.Select(h => h.name)));
                    }
                    report.Add("PASS " + name + ": every native piece completed from an empty companion inventory using repeated chest trips; exact ingredients consumed; station usable; bed sheltered and warmed by outdoor fire; human-sized entrance/aisle clear.");
                } catch (Exception e) {
                    report.Add("FAILED " + name + ": " + e.Message);
                    foreach (var plan in UnityEngine.Object.FindObjectsOfType(PlanBuildAdapter.PlanType).Cast<Component>()) {
                        var piece = PlanBuildAdapter.Original(plan);
                        foreach (var box in piece.GetComponentsInChildren<BoxCollider>()) {
                            if (box.isTrigger) continue;
                            Vector3 centre = plan.transform.position + plan.transform.rotation * piece.transform.InverseTransformPoint(box.transform.TransformPoint(box.center));
                            var half = Vector3.Scale(box.size, box.transform.lossyScale) * .45f;
                            var angle = plan.transform.rotation * Quaternion.Inverse(piece.transform.rotation) * box.transform.rotation;
                            foreach (var hit in Physics.OverlapBox(centre, half, angle, LayerMask.GetMask("piece", "static_solid", "Default"), QueryTriggerInteraction.Ignore)) {
                                if (!hit || hit.GetComponentInParent<Heightmap>() || hit.GetComponentInParent(PlanBuildAdapter.PlanType)) continue;
                                report.Add("Collision: " + piece.name + " at " + (plan.transform.position-site) + " vs " + hit.name + " parent=" + hit.transform.root.name + " at=" + (hit.transform.position-site));
                            }
                        }
                    }
                }
                File.WriteAllLines(output, report);
            }
            if (npc) ZNetScene.instance.Destroy(npc.gameObject);
            foreach (var p in UnityEngine.Object.FindObjectsOfType<Piece>().Where(p => p && p.GetCreator() == player.GetPlayerID()).ToArray()) ZNetScene.instance.Destroy(p.gameObject);
            yield return new WaitForSecondsRealtime(3);
            yield return WorkbenchSmoke.Run(plugin, player, Path.ChangeExtension(output, ".workbench.txt"));
        }
        private static void Capture(Vector3 site, string path)
        {
            var go = new GameObject("Rune isolated fixture camera");
            var camera = go.AddComponent<Camera>(); camera.CopyFrom(Camera.main);
            camera.transform.position = site + new Vector3(10, 11, -10);
            camera.transform.LookAt(site + new Vector3(0, 1, 3));
            var target = new RenderTexture(1280, 960, 24); camera.targetTexture = target;
            var previous = RenderTexture.active;
            try {
                camera.Render(); RenderTexture.active = target;
                var texture = new Texture2D(1280, 960, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0); texture.Apply();
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(texture)); UnityEngine.Object.Destroy(texture);
            } finally { RenderTexture.active = previous; camera.targetTexture = null; target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(go); }
        }
    }
}
