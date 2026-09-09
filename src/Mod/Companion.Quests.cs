using System;
using System.Collections.Generic;
using System.Linq;
using Rune.Shared;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        private CraftQuest craftingQuest;
        private float questPoll;
        private void LoadCraftingQuest()
        {
            try { string saved = view.GetZDO().GetString("rune.craftingQuest", ""); craftingQuest = saved.Length > 0 ? WireJson.Read<CraftQuest>(saved) : null; }
            catch { craftingQuest = null; }
            if (craftingQuest != null && craftingQuest.status == "Active") { craftingQuest.status = "Paused"; craftingQuest.note = "Ask me to craft this again to resume after loading."; }
        }
        private void SetQuestStatus(string status, string note)
        {
            if (craftingQuest == null) return;
            if (craftingQuest.status == "Complete" && status == "Paused") return;
            craftingQuest.status = status; craftingQuest.note = note; questPoll = 0;
            if (status == "Complete") {
                foreach (var need in craftingQuest.materials) { need.carried = need.required; need.missing = 0; }
                craftingQuest.gather = Array.Empty<MaterialNeed>();
            }
        }
        private string ClearCrafting()
        {
            if (craftPlan != null || (activeStep != null && IsCraftStep(activeStep)) || pendingSteps.Any(IsCraftStep)) Pause();
            suspendedJob = null; craftingQuest = null; questPoll = 0; Save();
            return "Crafting cleared. The material checklist is empty and related crafting work has stopped. Collected items are still in my inventory.";
        }
        private static bool IsCraftStep(PlanStep step) => step.action == "craft_item" || step.action == "gather_recipe" || step.action == "build_boat" || step.action == "planbuild_player" || step.action == "planbuild_self" || step.action == "finish_plan";
        public CraftQuest QuestSnapshot()
        {
            if (craftingQuest == null || craftingQuest.status == "Complete" || Time.unscaledTime < questPoll || !Player || !Ready) return craftingQuest;
            questPoll = Time.unscaledTime + 1;
            var cargo = Body.GetInventory().GetAllItems().Where(i => IsStorable(i) && i.m_dropPrefab).GroupBy(i => i.m_dropPrefab.name).ToDictionary(g => g.Key, g => g.Sum(i => i.m_stack));
            var stored = new Dictionary<string, int>();
            if (UseStoredMaterials && HasBase && Vector3.Distance(Player.transform.position, basePoint) < 45) {
                foreach (var chest in Chests()) foreach (var item in chest.GetInventory().GetAllItems().Where(i => IsStorable(i) && i.m_dropPrefab)) {
                    string key = item.m_dropPrefab.name; stored[key] = (stored.TryGetValue(key, out int count) ? count : 0) + item.m_stack;
                }
            }
            var available = new Dictionary<string, int>(cargo);
            foreach (var pair in stored) available[pair.Key] = (available.TryGetValue(pair.Key, out int count) ? count : 0) + pair.Value;
            var leaves = new Dictionary<string, MaterialNeed>();
            foreach (var need in craftingQuest.materials) {
                need.carried = cargo.TryGetValue(need.prefab, out int carried) ? carried : 0;
                need.stored = stored.TryGetValue(need.prefab, out int inBase) ? inBase : 0;
                need.missing = Math.Max(0, need.required - need.carried - need.stored);
                ExpandNeed(need.prefab, need.name, need.required, available, leaves, new HashSet<string>(), 0);
            }
            craftingQuest.gather = leaves.Values.OrderBy(n => n.name).ToArray();
            if (craftPlan != null) craftingQuest.note = craftNote;
            return craftingQuest;
        }
        private void ExpandNeed(string prefab, string name, int quantity, Dictionary<string, int> available, Dictionary<string, MaterialNeed> leaves, HashSet<string> chain, int depth)
        {
            int stock = available.TryGetValue(prefab, out int count) ? count : 0;
            int used = Math.Min(stock, quantity); available[prefab] = stock - used; quantity -= used;
            if (quantity <= 0) return;
            var recipe = KnownRecipe(prefab);
            if (recipe && depth < 6 && chain.Add(prefab)) {
                int output = Math.Max(1, recipe.m_amount), batches = (quantity + output - 1) / output;
                foreach (var ingredient in ActiveRequirements(recipe, recipe.m_resources))
                    ExpandNeed(ingredient.m_resItem.name, Localization.instance.Localize(ingredient.m_resItem.m_itemData.m_shared.m_name), ingredient.GetAmount(1) * batches, available, leaves, chain, depth + 1);
                available[prefab] += batches * output - quantity; chain.Remove(prefab); return;
            }
            if (!leaves.TryGetValue(prefab, out var need)) { need = new MaterialNeed { prefab = prefab, name = name }; leaves.Add(prefab, need); }
            need.required += quantity; need.missing += quantity;
        }
    }
}
