using System;
using System.Linq;
using System.Collections.Generic;
using Rune.Shared;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        private readonly Queue<PlanStep> pendingSteps = new Queue<PlanStep>();
        private PlanStep activeStep;
        private bool executingStep;
        private int planTotal, planDone;
        public string PlanLabel = "";
        public string NextPlanLabel => string.Join(" → ", pendingSteps.Take(2).Select(s => Rules.DescribeAction(s.action, s.amount, s.item)));
        public string QueuePlan(PlanStep[] steps, string objective = "")
        {
            LastOrderAccepted = true;
            string error = Rules.ValidatePlan(steps); if (error.Length > 0) { LastOrderAccepted = false; return error; }
            pendingSteps.Clear(); activeStep = null; planDone = 0; planTotal = steps.Length;
            foreach (var step in steps) pendingSteps.Enqueue(step);
            Objective = Rules.NormalizeObjective(objective.Length > 0 ? objective : "Complete " + steps.Length + " step plan");
            PlanLabel = "Plan ready • " + planTotal + " steps";
            BeginNextStep(); Save(); return PlanLabel;
        }
        private void ClearPlan(string reason = "Plan stopped", bool preserve = false) {
            if (preserve || reason.StartsWith("Plan blocked")) PreserveJob(reason);
            else if (!restoringJob) suspendedJob = null;
            pendingSteps.Clear(); activeStep = null; PlanLabel = reason;
        }
        private void BeginNextStep()
        {
            if (pendingSteps.Count == 0) { activeStep = null; PlanLabel = "Plan complete • " + planDone + " steps"; Say(DisplayName + ": " + PlanLabel); return; }
            activeStep = pendingSteps.Dequeue();
            PlanLabel = "Step " + (planDone + 1) + "/" + planTotal + " • " + activeStep.action.Replace('_', ' ') + (activeStep.item.Length > 0 ? " " + activeStep.item : "");
            executingStep = true;
            string response;
            try { response = Order(activeStep.action, activeStep.amount, activeStep.item); }
            finally { executingStep = false; }
            if (!LastOrderAccepted) { ClearPlan("Plan blocked: " + response); Save(); Say(PlanLabel); }
        }
        private void AdvancePlan()
        {
            if (activeStep == null || orderFailure.Length > 0 || (mode != "follow" && mode != "stay")) return;
            if ((activeStep.action == "return" || activeStep.action.StartsWith("gather_")) && CargoCount > 0) { ClearPlan("Plan blocked: make inventory space to receive my cargo."); Say(PlanLabel); return; }
            if ((activeStep.action == "gather_wood" || activeStep.action == "gather_stone" || activeStep.action == "gather_item") && gathered < goal) { ClearPlan("Plan blocked: not enough reachable materials."); Say(PlanLabel); return; }
            planDone++; activeStep = null; BeginNextStep(); Save();
        }
    }
}
