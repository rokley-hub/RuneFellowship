using System.Linq;
using Rune.Shared;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        public string DiagnosticCommand = "", DiagnosticAction = "";
        public bool DiagnosticRejected;
        private bool diagnosticSafety, diagnosticCombat;
        public CompanionState DiagnosticState()
        {
            // A compact snapshot once per bridge update, never inventory scans added to every frame.
            string outcome = "active";
            if (orderFailure.Length > 0 || PlanLabel.StartsWith("Plan blocked") ||
                ((DiagnosticAction == "craft_item" || DiagnosticAction == "gather_recipe" || DiagnosticAction == "build_boat" || DiagnosticAction == "planbuild_player" || DiagnosticAction == "planbuild_self" || DiagnosticAction == "finish_plan") && craftingQuest != null && craftingQuest.status == "Blocked")) outcome = "blocked";
            else if (DiagnosticAction == "run_plan" && PlanLabel.StartsWith("Plan complete")) outcome = "completed";
            else if ((DiagnosticAction == "craft_item" || DiagnosticAction == "build_boat" || DiagnosticAction == "planbuild_player" || DiagnosticAction == "planbuild_self" || DiagnosticAction == "finish_plan") && craftingQuest != null && craftingQuest.status == "Complete") outcome = "completed";
            else if (mode == "follow" && !autoPickup && (DiagnosticAction == "gather_wood" || DiagnosticAction == "gather_stone" || DiagnosticAction == "gather_item") && gathered >= goal && CargoCount == 0) outcome = "completed";
            else if (mode == "follow" && DiagnosticAction == "pickup_equip" && gathered == 1) outcome = "completed";
            else if ((mode == "follow" || mode == "stay") && (DiagnosticAction == "return" || DiagnosticAction == "gather_recipe") && CargoCount == 0 && (!deliverKept || !Body.GetInventory().GetAllItems().Any(i => i.m_customData.ContainsKey("rune.kept") && !ReservedSupply(i))) && (DiagnosticAction != "gather_recipe" || (craftingQuest != null && craftingQuest.status == "Complete"))) outcome = "completed";
            else if ((mode == "follow" || mode == "stay") && activeStep == null && Rune.Shared.Rules.PlanActions.Contains(DiagnosticAction)) outcome = "ended-unverified";
            if (DiagnosticRejected) outcome = "rejected";
            return new CompanionState {
                id = Id, name = DisplayName, appearance = Appearance, task = TaskLabel, health = Body.GetHealth(), maxHealth = Body.GetMaxHealth(), cargo = CargoCount, hasBase = HasBase, plan = PlanLabel, objective = ObjectiveOngoing ? Objective : "", next = NextPlanLabel, quest = QuestSnapshot(), tools = ToolStatus,
                diagnosticsVersion = 1, commandId = DiagnosticCommand, outcome = outcome, workPhase = maintenanceNote.Length > 0 ? "maintenance" : mode,
                workProgress = gathered + "/" + goal + "|" + string.Join(";", Body.GetInventory().GetAllItems().Select(i => i.m_shared.m_name + ":" + i.m_stack)),
                x = transform.position.x, y = transform.position.y, z = transform.position.z, safetyPaused = diagnosticSafety, inCombat = diagnosticCombat
            };
        }
    }
}
