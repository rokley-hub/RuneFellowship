using System;
using System.Linq;
using Rune.Shared;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        [Serializable] public sealed class SavedPiece
        {
            public string prefab = "";
            public float x, y, z;
            public Vector3 Position => new Vector3(x, y, z);
        }
        [Serializable] public sealed class SavedJob
        {
            public PlanStep[] steps = Array.Empty<PlanStep>();
            public SavedPiece[] pieces = Array.Empty<SavedPiece>();
            public string objective = "", reason = "", name = "";
            public float x, y, z;
            public bool construction;
        }
        private SavedJob suspendedJob;
        private bool restoringJob;

        private SavedJob CaptureJob(string reason)
        {
            PlanStep current = activeStep;
            if (craftPlan != null) current = new PlanStep { action = craftPlan.Action, item = craftPlan.Name, amount = 1 };
            else if (mode == "gather" && !autoPickup) current = new PlanStep {
                action = resource == "Wood" ? "gather_wood" : resource == "Stone" ? "gather_stone" : "gather_item",
                item = resource, amount = Math.Max(1, goal - gathered)
            };
            else if (mode == "return" && (CargoCount > 0 || deliverKept)) current = new PlanStep { action = "return", amount = 1 };
            // A completed recipe must never be crafted a second time after a save.
            if (current != null && IsCraftStep(current) && craftPlan == null && craftingQuest?.status == "Complete") current = null;
            if (current != null && current.action.StartsWith("gather_") && craftPlan == null && gathered >= goal && CargoCount == 0) current = null;
            if (current?.action == "return" && CargoCount == 0 && !deliverKept) current = null;
            if (current == null && pendingSteps.Count == 0) return null;
            var snapshot = new SavedJob {
                objective = Objective, reason = reason,
                steps = (current == null ? Array.Empty<PlanStep>() : new[] { current }).Concat(pendingSteps).ToArray()
            };
            if (craftPlan?.Construction != null) {
                var job = craftPlan.Construction;
                snapshot.construction = true; snapshot.name = craftPlan.Name;
                snapshot.x = job.Origin.x; snapshot.y = job.Origin.y; snapshot.z = job.Origin.z;
                var pieces = job.Pieces.Where(p => p).Select(p => new SavedPiece {
                    prefab = PlanBuildAdapter.Original(p).gameObject.name.Split('(')[0],
                    x = p.transform.position.x, y = p.transform.position.y, z = p.transform.position.z
                }).ToList();
                if (job.PendingPrefab != null) pieces.Add(new SavedPiece { prefab = job.PendingPrefab,
                    x = job.PendingPosition.x, y = job.PendingPosition.y, z = job.PendingPosition.z });
                snapshot.pieces = pieces.ToArray();
            }
            return snapshot.steps.Length == 0 ? null : snapshot;
        }
        private void PreserveJob(string reason)
        {
            var snapshot = CaptureJob(reason);
            if (snapshot != null) suspendedJob = snapshot;
        }
        private void SaveJob()
        {
            var snapshot = CaptureJob("Paused after loading. Say resume task to continue.") ?? suspendedJob;
            view.GetZDO().Set("rune.savedJob", snapshot == null ? "" : WireJson.Write(snapshot));
        }
        private void LoadJob()
        {
            try {
                string json = view.GetZDO().GetString("rune.savedJob", "");
                suspendedJob = json.Length == 0 ? null : WireJson.Read<SavedJob>(json);
                if (suspendedJob != null && (suspendedJob.steps == null || suspendedJob.steps.Length > 8 || suspendedJob.pieces == null || suspendedJob.pieces.Length > 256)) suspendedJob = null;
            } catch { suspendedJob = null; }
            if (suspendedJob != null) PlanLabel = "Paused job • say resume task";
        }
        private string ResumeJob()
        {
            var saved = suspendedJob;
            if (saved == null || saved.steps.Length == 0) { LastOrderAccepted = false; return "There is no saved unfinished task to resume."; }
            // Validate again after body/permission changes. Resume never replays a placement.
            if (!AllowCrafting && saved.steps.Any(IsCraftStep)) { LastOrderAccepted = false; return "Crafting and building are disabled for this companion. Enable them before resuming this job."; }
            if (!AllowBaseWork && saved.steps.Any(s => s.action == "store_cargo" || s.action == "sort_storage" || s.action == "cook_food" || s.action == "manage_base")) { LastOrderAccepted = false; return "Base work is disabled for this companion."; }
            if (Appearance == "wolf" && saved.steps.Any(IsCraftStep)) { LastOrderAccepted = false; return "A wolf cannot resume crafting or construction."; }
            var remaining = saved.steps.Skip(1).ToArray();
            if (saved.construction) {
                var origin = new Vector3(saved.x, saved.y, saved.z);
                if (PlanBuildAdapter.PlanType == null || Vector3.Distance(Player.transform.position, origin) > 60) {
                    LastOrderAccepted = false; return "Return to the original building site with PlanBuild loaded, then say resume task.";
                }
                if (BuildingHammer() == null) { LastOrderAccepted = false; return "I need a usable hammer before resuming construction."; }
                var plans = UnityEngine.Object.FindObjectsOfType(PlanBuildAdapter.PlanType).Cast<Component>()
                    .Where(p => p && p.GetComponent<Piece>().GetCreator() == Owner).ToArray();
                var job = new ConstructionJob { Origin = origin };
                foreach (var piece in saved.pieces) {
                    var plan = plans.FirstOrDefault(p => Vector3.Distance(p.transform.position, piece.Position) < .1f
                        && PlanBuildAdapter.Original(p).gameObject.name.Split('(')[0] == piece.prefab);
                    if (plan) { if (!job.Pieces.Contains(plan)) job.Pieces.Add(plan); continue; }
                    bool completed = Physics.OverlapSphere(piece.Position, 4, LayerMask.GetMask("piece"))
                        .Select(c => c.GetComponentInParent<Piece>()).Any(p => p && !p.GetComponent(PlanBuildAdapter.PlanType)
                        && p.gameObject.name.Split('(')[0] == piece.prefab && Vector3.Distance(p.transform.position, piece.Position) < .1f);
                    if (!completed) { LastOrderAccepted = false; return "A saved plan is missing or its terrain is not loaded. Inspect the original site; I will not place a duplicate."; }
                }
                pendingSteps.Clear(); foreach (var step in remaining) pendingSteps.Enqueue(step);
                activeStep = saved.steps[0]; planDone = 0; planTotal = saved.steps.Length;
                job.Total = job.Pieces.Count;
                craftPlan = new CraftPlan { Name = saved.name, Action = "finish_plan", Construction = job, Started = Time.time, Requirements = Array.Empty<Piece.Requirement>() };
                craftingQuest = new CraftQuest { item = saved.name, action = "finish_plan", status = "Active" };
                RefreshConstructionMaterials(job);
                mode = "craft_plan"; target = null; autoPickup = false; followingOrder = false;
                // Let newly loaded PlanBuild pieces initialize their support cache.
                SetCombatTarget(null); ai.SetFollowTarget(null); craftAt = Time.time + 2;
                Objective = saved.objective; PlanLabel = "Resuming construction at the original site";
                suspendedJob = null; Save(); return "Resuming the saved plans at their original location. Completed pieces and deposited supplies are kept.";
            }
            suspendedJob = null; restoringJob = true;
            try { return QueuePlan(saved.steps, saved.objective); }
            finally { restoringJob = false; }
        }
    }
}
