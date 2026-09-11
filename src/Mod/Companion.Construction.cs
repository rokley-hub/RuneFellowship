using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Rune.Shared;

namespace Rune.Mod
{
    public partial class Companion
    {
        private void Say(string text) => Plugin.Instance.Say(text, Id);
        private sealed class ConstructionJob
        {
            internal readonly List<Component> Pieces = new List<Component>();
            internal Component Target;
            internal Vector3 Origin, PendingPosition;
            internal Vector3 StandingPoint;
            internal bool HasStandingPoint;
            internal bool DirectApproach;
            internal float ApproachAt;
            internal string PendingPrefab;
            internal float CheckAt, WalkStarted;
            internal int Completed, Total;
        }
        private static BoxCollider placementProbe;
        private string StartConstruction(string action, string name)
        {
            if (Rune.Shared.Rules.IsWolf(Appearance)) return RejectCraft("A wolf cannot use building tools.");
            if (BuildingHammer() == null) return RejectCraft("I need a usable hammer. Ask me to craft a hammer first, or lend me one.");
            if (PlanBuildAdapter.PlanType == null) return RejectCraft("PlanBuild is not loaded. Install it and restart Valheim.");
            try {
                var reference = action == "planbuild_self" ? transform : Player.transform;
                Vector3 origin = reference.position;
                Quaternion rotation = Quaternion.Euler(0, reference.eulerAngles.y, 0);
                var job = new ConstructionJob { Origin = origin };
                List<PlanBuildAdapter.Entry> entries = null;
                if (action == "finish_plan") {
                    job.Pieces.AddRange(UnityEngine.Object.FindObjectsOfType(PlanBuildAdapter.PlanType).Cast<Component>()
                        .Where(p => p && Vector3.Distance(p.transform.position, origin) <= 25 && p.GetComponent<Piece>().GetCreator() == Owner));
                    if (job.Pieces.Count == 0 || job.Pieces.Count > 256) return RejectCraft("There must be 1 to 256 of your unfinished PlanBuild pieces within 25 metres of you.");
                    name = "nearby PlanBuild plans";
                } else if (IsWorkbench(name) && action == "craft_item") {
                    var probe = new CraftPlan { BuildOrigin = origin, BuildRotation = rotation };
                    if (!FindWorkbenchSpot(probe, out Vector3 position)) return RejectCraft("I cannot find a clear level workbench site within six metres of you.");
                    var prefab = Jotunn.Managers.PrefabManager.Instance.GetPrefab("piece_workbench_planned");
                    if (!prefab) return RejectCraft("PlanBuild's workbench plan is unavailable.");
                    entries = new List<PlanBuildAdapter.Entry> { new PlanBuildAdapter.Entry { Prefab = prefab, Position = position, Rotation = rotation } };
                } else entries = PlanBuildAdapter.Resolve(name, origin, rotation);
                if (entries != null) {
                    foreach (var entry in entries) {
                        var piece = PlanBuildAdapter.Original(entry.Prefab.GetComponent(PlanBuildAdapter.PlanType));
                        string problem = ConstructionAllowed(piece, entry.Position);
                        if (problem.Length > 0) return RejectCraft(problem);
                        if (!ConstructionSpace(piece, entry.Position, entry.Rotation, null)) return RejectCraft("The blueprint overlaps an existing object at this location. Move to a clear site, or place the plan precisely with PlanBuild and ask me to finish it.");
                    }
                    foreach (var entry in entries) job.Pieces.Add(PlanBuildAdapter.Place(entry, Owner));
                }
                job.Total = job.Pieces.Count;
                craftPlan = new CraftPlan { Name = name, Action = action, Construction = job, Started = Time.time, Requirements = Array.Empty<Piece.Requirement>() };
                craftingQuest = new CraftQuest { item = name, action = action, status = "Active" };
                RefreshConstructionMaterials(job);
                autoPickup = false; target = null; mode = "craft_plan"; SetCombatTarget(null); ai.SetFollowTarget(null);
                craftAt = Time.time + 2; craftNote = "Checking PlanBuild support"; questPoll = 0; Save();
                return "PlanBuild: " + name + ", " + job.Total + " pieces, anchored at " + (action == "planbuild_self" ? "my" : "your") + " position when you asked. I'll gather and build in stages. Follow or clear crafting stops me; unfinished plans and deposited materials remain. The checklist shows remaining materials.";
            } catch (Exception e) { return RejectCraft("PlanBuild: " + (e.InnerException?.Message ?? e.Message)); }
        }
        private string ConstructionAllowed(Piece piece, Vector3 position)
        {
            if (!BuildingKnowledge.Known(Player, piece)) return "This piece is not unlocked by your current building requirements: " + (piece ? Localization.instance.Localize(piece.m_name) : "missing piece") + ". Discover its materials and required building station first.";
            if (piece.m_resources.Any(r => r.m_resItem && !Player.IsMaterialKnown(r.m_resItem.m_itemData.m_shared.m_name))) return "Discover the materials for " + Localization.instance.Localize(piece.m_name) + " first.";
            if (Location.IsInsideNoBuildLocation(position) || !PrivateArea.CheckAccess(position, 0, false, false)) return "The building position is protected or disallows building.";
            if (!Heightmap.GetHeight(position, out _)) return "The building terrain is not loaded.";
            return "";
        }
        private static bool ConstructionSpace(Piece piece, Vector3 position, Quaternion rotation, Component ownPlan)
        {
            // Prefab colliders have no active PhysX shape. A trigger-only query box
            // supplies that shape without spawning a buildable piece or blocking AI.
            if (!placementProbe) {
                var probe = new GameObject("Rune placement query") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                probe.transform.position = new Vector3(0, -100000, 0);
                placementProbe = probe.AddComponent<BoxCollider>(); placementProbe.isTrigger = true;
            }
            foreach (var box in piece.GetComponentsInChildren<BoxCollider>()) {
                if (box.isTrigger) continue;
                Vector3 centre = position + rotation * piece.transform.InverseTransformPoint(box.transform.TransformPoint(box.center));
                Vector3 half = Vector3.Scale(box.size, box.transform.lossyScale) * .45f;
                Quaternion angle = rotation * Quaternion.Inverse(piece.transform.rotation) * box.transform.rotation;
                placementProbe.size = Vector3.Scale(box.size, box.transform.lossyScale);
                foreach (var collider in Physics.OverlapBox(centre, half, angle, LayerMask.GetMask("piece", "static_solid", "Default"), QueryTriggerInteraction.Ignore)) {
                    if (!collider || collider.GetComponentInParent<Heightmap>()) continue;
                    if (collider.GetComponentInParent(PlanBuildAdapter.PlanType)) continue;
                    var other = collider.GetComponentInParent<Piece>();
                    // Valheim deliberately allows clipping between structural parts
                    // (gable beams, roofs, walls). Keep furniture and stations clear.
                    if (other && StructuralPiece(piece) && StructuralPiece(other)) {
                        bool duplicate = other.gameObject.name.Split('(')[0] == piece.gameObject.name && Vector3.Distance(other.transform.position, position) < .1f && Quaternion.Angle(other.transform.rotation, rotation) < 5;
                        if (!duplicate) continue;
                        return false;
                    }
                    // Match the native placement penetration tolerance for snapped
                    // building joints, while still rejecting objects occupying the site.
                    float tolerance = collider.GetComponentInParent<Piece>() ? .2f : .01f;
                    if (Physics.ComputePenetration(placementProbe, centre, angle, collider, collider.transform.position, collider.transform.rotation, out _, out float depth) && depth > tolerance + .001f) return false;
                }
            }
            return true;
        }
        private void RefreshConstructionMaterials(ConstructionJob job)
        {
            var remaining = new Dictionary<string, Piece.Requirement>();
            foreach (var plan in job.Pieces.Where(p => p)) {
                var counts = PlanBuildAdapter.Remaining(plan);
                foreach (var req in PlanBuildAdapter.Original(plan).m_resources.Where(r => r.m_resItem)) {
                    int amount = Math.Max(0, counts[req.m_resItem.m_itemData.m_shared.m_name]);
                    if (!remaining.TryGetValue(req.m_resItem.name, out var total)) remaining[req.m_resItem.name] = total = new Piece.Requirement { m_resItem = req.m_resItem, m_amount = 0 };
                    total.m_amount += amount;
                }
            }
            craftingQuest.materials = Plugin.RecipeMaterials(remaining.Values.ToArray()); questPoll = 0;
        }
        private bool PrepareConstruction(float dt)
        {
            var job = craftPlan.Construction;
            if (Time.time < craftAt) { ai.StopMoving(); return false; }
            if (Vector3.Distance(Player.transform.position, job.Origin) > 60) { BlockCraft("Stay near the building site so its terrain and plans remain loaded. Ask me to finish the plan when you return."); return false; }
            if (job.PendingPrefab != null) {
                bool built = Physics.OverlapSphere(job.PendingPosition, 4, LayerMask.GetMask("piece")).Select(c => c.GetComponentInParent<Piece>()).Any(p => p && !p.GetComponent(PlanBuildAdapter.PlanType) && p.gameObject.name.Split('(')[0] == job.PendingPrefab && Vector3.Distance(p.transform.position, job.PendingPosition) < .15f);
                if (!built) { BlockCraft("PlanBuild did not confirm the completed piece. Inspect the plan before trying again."); return false; }
                job.Completed++; job.PendingPrefab = null; job.Target = null; RefreshConstructionMaterials(job);
            }
            if (!job.Pieces.Any(p => p)) {
                if (job.Completed != job.Total) { BlockCraft("Some plans were removed or completed externally. Inspect the site; I have stopped this job."); return false; }
                SetQuestStatus("Complete", "PlanBuild construction completed."); craftPlan = null; mode = "stay"; anchor = transform.position; Save(); Say("Finished the PlanBuild construction. Building pieces still follow Valheim's shelter, fire and support rules."); return false;
            }
            if (BuildingHammer() == null) { BlockCraft("My hammer is missing or broken. Repair or replace it, then ask me to finish the plan."); return false; }
            if (!job.Target) {
                job.Target = job.Pieces.Where(p => p && PlanBuildAdapter.Supported(p)).OrderBy(p => PlanBuildAdapter.Original(p).GetComponent<Door>() ? 3 : PlanBuildAdapter.Original(p).GetComponent<CraftingStation>() ? 0 : 1).ThenBy(p => p.transform.position.y).FirstOrDefault(p => {
                    var station = PlanBuildAdapter.Original(p).m_craftingStation;
                    return !station || CraftingStation.HaveBuildStationInRange(station.m_name, p.transform.position);
                });
                job.WalkStarted = 0;
                job.HasStandingPoint = false; job.ApproachAt = 0;
                if (!job.Target) { BlockCraft("The remaining plans need structural support or a required crafting station in range. Fix those prerequisites, then say finish plan."); return false; }
            }
            if (craftPlan.Dependencies.Count > 0) return true;
            var original = PlanBuildAdapter.Original(job.Target);
            string error = ConstructionAllowed(original, job.Target.transform.position);
            if (error.Length > 0) { BlockCraft(error); return false; }
            var remaining = PlanBuildAdapter.Remaining(job.Target);
            // Fetch one material in bounded batches; deposit each batch into the real plan.
            // Large blueprints never need to fit in Rune's inventory in one trip.
            var needed = original.m_resources.FirstOrDefault(r => r.m_resItem && remaining[r.m_resItem.m_itemData.m_shared.m_name] > 0);
            if (needed == null) craftPlan.Requirements = Array.Empty<Piece.Requirement>();
            else {
                int held = UsableCount(needed.m_resItem.name);
                float weight = Math.Max(.01f, needed.m_resItem.m_itemData.m_shared.m_weight);
                int room = Math.Max(0, (int)((300 - Body.GetInventory().GetTotalWeight()) / weight));
                string resourceName = needed.m_resItem.m_itemData.m_shared.m_name;
                int pieceNeed = remaining[resourceName];
                // Fetch a useful batch for upcoming pieces instead of walking back
                // to storage for every two-wood wall. Never exceed the live job cost.
                int batchNeed = held >= pieceNeed ? pieceNeed : job.Pieces.Where(p => p).Sum(p => PlanBuildAdapter.Remaining(p).TryGetValue(resourceName, out int n) ? Math.Max(0, n) : 0);
                int count = Math.Min(batchNeed, Math.Min(20, held + room));
                if (count == 0) { BlockCraft("My inventory is at the 300 weight construction limit. Free space, then ask me to finish the plan."); return false; }
                craftPlan.Requirements = new[] { new Piece.Requirement { m_resItem = needed.m_resItem, m_amount = count } };
            }
            craftNote = "PlanBuild " + job.Completed + "/" + job.Total + ": " + Localization.instance.Localize(original.m_name);
            return true;
        }
        private void BuildConstruction(float dt)
        {
            var job = craftPlan.Construction; var plan = job.Target;
            if (!plan) return;
            Vector3 destination = plan.transform.position;
            if (Vector3.Distance(transform.position, destination) > 3.5f) {
                if (job.WalkStarted == 0) job.WalkStarted = Time.time;
                if (Time.time - job.WalkStarted > 35) { BlockCraft("I cannot reach this planned piece. Add a safe route or scaffolding, then ask me to finish the plan."); return; }
                if (Time.time >= job.ApproachAt) {
                    job.ApproachAt = Time.time + 3;
                    job.HasStandingPoint = ConstructionApproach(destination, out job.StandingPoint, out job.DirectApproach);
                }
                craftNote = "Walking to planned piece " + (job.Completed + 1) + "/" + job.Total;
                if (job.HasStandingPoint && job.DirectApproach && ClearConstructionWalk(job.StandingPoint)) {
                    var direction = job.StandingPoint - transform.position; direction.y = 0; ai.MoveTowards(direction.normalized, true);
                }
                else if (job.HasStandingPoint) Move(dt, job.StandingPoint, .25f);
                else {
                    float height = Mathf.Abs(destination.y - transform.position.y);
                    float horizontalReach = Mathf.Sqrt(Mathf.Max(.25f, 3.2f * 3.2f - height * height));
                    Move(dt, destination, Mathf.Min(2.5f, horizontalReach));
                }
                return;
            }
            ai.StopMoving();
            var original = PlanBuildAdapter.Original(plan);
            string error = ConstructionAllowed(original, destination);
            if (error.Length > 0 || !ConstructionSpace(original, destination, plan.transform.rotation, plan)) { BlockCraft(error.Length > 0 ? error : "An existing object blocks this planned piece. Clear it manually, then ask me to finish the plan."); return; }
            var net = plan.GetComponent<ZNetView>();
            if (!net || !net.IsValid() || !net.IsOwner()) { BlockCraft("This plan is not owned by this game client. Try again when it is available locally."); return; }
            PlanBuildAdapter.Deposit(plan, Body); RefreshConstructionMaterials(job); Save(); craftAt = Time.time + 1;
            if (!PlanBuildAdapter.Funded(plan)) return;
            if (!PlanBuildAdapter.Supported(plan)) { job.Target = null; return; }
            if (original.m_craftingStation && !CraftingStation.HaveBuildStationInRange(original.m_craftingStation.m_name, transform.position)) { BlockCraft("I need the required building station in range of where I am standing."); return; }
            var hammer = BuildingHammer(); if (hammer == null) { BlockCraft("I need a usable hammer to finish the piece."); return; }
            Body.EquipItem(hammer, false);
            job.PendingPosition = destination; job.PendingPrefab = original.gameObject.name;
            PlanBuildAdapter.Build(plan, Owner);
            if (hammer.m_shared.m_useDurability) hammer.m_durability = Math.Max(0, hammer.m_durability - hammer.m_shared.m_useDurabilityDrain);
            if (!string.IsNullOrEmpty(hammer.m_shared.m_attack?.m_attackAnimation)) Body.GetZAnim().SetTrigger(hammer.m_shared.m_attack.m_attackAnimation);
            Save();
        }
        private bool ConstructionApproach(Vector3 targetPosition, out Vector3 result, out bool direct)
        {
            direct = false;
            var candidates = new List<Vector3>();
            int mask = LayerMask.GetMask("terrain", "piece", "static_solid", "Default");
            foreach (float radius in new[] { 0f, .75f, 1.5f, 2.3f, 2.8f }) for (int i = 0; i < (radius == 0 ? 1 : 12); i++) {
                float angle = i * Mathf.PI / 6;
                var point = targetPosition + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                // Look for a foothold at the companion's walking level, not on the roof.
                point.y = transform.position.y + .65f;
                if (!Physics.Raycast(point, Vector3.down, out var ground, 2f, mask, QueryTriggerInteraction.Ignore) || ground.normal.y < .75f) continue;
                point.y = ground.point.y + .05f;
                if (point.y < WaterAt(point) || Vector3.Distance(point, targetPosition) > 3.15f) continue;
                if (EffectArea.IsPointInsideArea(point, EffectArea.Type.Burning, .35f)) continue;
                var occupied = Physics.OverlapCapsule(point + Vector3.up * .4f, point + Vector3.up * 1.45f, .35f, mask, QueryTriggerInteraction.Ignore);
                if (occupied.Any(c => c && !c.GetComponentInParent<Character>() && !c.GetComponentInParent<Heightmap>() && !c.GetComponentInParent(PlanBuildAdapter.PlanType))) continue;
                candidates.Add(point);
            }
            candidates = candidates.OrderBy(p => Vector3.SqrMagnitude(p - transform.position)).ToList();
            foreach (var point in candidates) {
                if (ClearConstructionWalk(point)) { result = point; direct = true; return true; }
            }
            var path = new List<Vector3>();
            foreach (var point in candidates) {
                path.Clear();
                // Native AI permits a partial path. Its actual endpoint must still
                // be within hammer reach, not merely close to the requested foothold.
                if (!Pathfinding.instance.GetPath(transform.position, point, path, ai.m_pathAgentType, false, true, false)) continue;
                var endpoint = path.Count > 0 ? path[path.Count - 1] : point;
                if (Vector3.Distance(endpoint, targetPosition) <= 3.15f && Vector3.Distance(endpoint, point) <= .4f && Mathf.Abs(endpoint.y - point.y) < .25f) { result = point; return true; }
            }
            result = default; return false;
        }
        private bool ClearConstructionWalk(Vector3 point)
        {
            var from = transform.position; var offset = point - from;
            if (offset.magnitude > 6 || Mathf.Abs(offset.y) > .5f) return false;
            int mask = LayerMask.GetMask("piece", "static_solid", "Default");
            var hits = Physics.CapsuleCastAll(from + Vector3.up * .4f, from + Vector3.up * 1.45f, .35f, offset.normalized, offset.magnitude, mask, QueryTriggerInteraction.Ignore);
            if (hits.Any(h => h.collider && !h.collider.GetComponentInParent<Character>() && !h.collider.GetComponentInParent<Heightmap>() && !h.collider.GetComponentInParent(PlanBuildAdapter.PlanType))) return false;
            int samples = Math.Max(1, Mathf.CeilToInt(offset.magnitude / .5f));
            for (int i = 0; i <= samples; i++) {
                var foot = Vector3.Lerp(from, point, i / (float)samples);
                if (EffectArea.IsPointInsideArea(foot, EffectArea.Type.Burning, .35f)) return false;
                if (!Physics.Raycast(foot + Vector3.up * .45f, Vector3.down, out var ground, .8f, mask | LayerMask.GetMask("terrain"), QueryTriggerInteraction.Ignore) || ground.normal.y < .75f || Mathf.Abs(ground.point.y - foot.y) > .3f || ground.point.y < WaterAt(foot)) return false;
            }
            return true;
        }
        private static bool StructuralPiece(Piece piece) => !piece.m_noClipping && piece.GetComponent<WearNTear>() && !piece.GetComponents<MonoBehaviour>().OfType<Interactable>().Any();
    }
}


