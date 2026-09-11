using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using Jotunn.Managers;
using Rune.Shared;

namespace Rune.Mod
{
    public partial class Companion
    {
        private CraftPlan craftPlan;
        private string craftNote = "Planning materials";
        private float craftAt;
        private sealed class CraftPlan
        {
            public string Name, Action;
            public Recipe Recipe;
            public GameObject Boat;
            public Piece.Requirement[] Requirements;
            public float Started;
            public bool Workbench;
            public ConstructionJob Construction;
            public Vector3 BuildOrigin, BuildPosition;
            public Quaternion BuildRotation;
            public float BuildWalkStarted;
            public float BuildValidateAt;
            public readonly Stack<Recipe> Dependencies = new Stack<Recipe>();
        }
        private int UsableCount(string prefab) => Body.GetInventory().GetAllItems().Where(i => IsStorable(i) && i.m_dropPrefab && i.m_dropPrefab.name == prefab).Sum(i => i.m_stack);
        private Recipe KnownRecipe(string item) => ObjectDB.instance.m_recipes.FirstOrDefault(r => r && r.m_enabled && r.m_item && Player.IsRecipeKnown(r.m_item.m_itemData.m_shared.m_name) && (Normalize(r.m_item.name) == Normalize(item) || Normalize(Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name)) == Normalize(item)));
        private string StartCraftPlan(string action, string item)
        {
            if (IsWorkbench(item) && action != "gather_recipe") return StartConstruction(action, item);
            if (Rune.Shared.Rules.IsWolf(Appearance)) return RejectCraft("A wolf can fetch loose items and fight, but cannot craft or build a workbench or boat.");
            bool workbench = IsWorkbench(item);
            string boatName = Normalize(item) == "boat" || Normalize(item) == "karve" ? "Karve" : Normalize(item) == "raft" ? "Raft" : Normalize(item) == "longship" ? "VikingShip" : "";
            var boat = workbench ? PrefabManager.Instance.GetPrefab("piece_workbench") : boatName.Length > 0 ? PrefabManager.Instance.GetPrefab(boatName) : null;
            var recipe = boat ? null : KnownRecipe(item);
            if (!boat && !recipe) return RejectCraft("I couldn't match that to a crafting recipe you have learned. Use its exact in-game item name. Building placement supports workbench, raft, karve and longship.");
            var requirements = boat ? boat.GetComponent<Piece>().m_resources : recipe.m_resources;
            if (boat && requirements.Any(r => r.m_resItem && !Player.IsMaterialKnown(r.m_resItem.m_itemData.m_shared.m_name))) return RejectCraft("You have not discovered every ingredient for that structure yet.");
            var next = new CraftPlan { Name = item, Action = action, Boat = boat, Recipe = recipe, Requirements = requirements, Started = Time.time, Workbench = workbench, BuildOrigin = Player.transform.position, BuildRotation = Quaternion.Euler(0, Player.transform.eulerAngles.y, 0) };
            if (workbench && !BuildingKnowledge.Known(Player, boat.GetComponent<Piece>())) return RejectCraft("You need to discover the workbench building piece first.");
            if (workbench && action != "gather_recipe") {
                if (BuildingHammer() == null) return RejectCraft("I need a usable hammer in my inventory to build the workbench. Ask me to craft a hammer first, or lend me one.");
                if (!FindWorkbenchSpot(next, out next.BuildPosition)) return RejectCraft("I cannot find clear, level, permitted ground or flooring within six metres of this spot. Move to a clearer building site and ask again.");
            }
            autoPickup = false; target = null; mode = "craft_plan"; ai.SetFollowTarget(null);
            craftPlan = next;
            craftingQuest = new CraftQuest { item = boat ? Localization.instance.Localize(boat.GetComponent<Piece>().m_name) : Localization.instance.Localize(recipe.m_item.m_itemData.m_shared.m_name), action = action, status = "Active", materials = Plugin.RecipeMaterials(ActiveRequirements(recipe, requirements)) };
            craftNote = "Planning " + craftingQuest.item; craftAt = 0; questPoll = 0; QuestSnapshot(); Save();
            string list = string.Join(", ", craftingQuest.materials.Select(r => r.required + " " + r.name));
            string missing = string.Join(", ", craftingQuest.gather.Select(r => r.missing + " " + r.name));
            return "Working on " + craftingQuest.item + ". Recipe: " + list + ". " + (missing.Length > 0 ? "Still needed: " + missing + ". " : "The ingredients are in my cargo or accessible base storage. ") + (workbench && action != "gather_recipe" ? "I'll place it near where you are standing now. An exposed workbench still needs shelter for crafting. " : "") + "The crafting checklist is in the Fellowship overlay. I'll gather reachable materials and report anything blocked. Say clear crafting to cancel.";
        }
        private string RejectCraft(string reason) { LastOrderAccepted = false; return reason + " No crafting order started."; }
        private void BlockCraft(string reason) { PreserveJob(reason); bool building = craftPlan?.Construction != null; if (activeStep != null) ClearPlan("Plan blocked: " + reason); SetQuestStatus("Blocked", reason); craftPlan = null; mode = "stay"; anchor = transform.position; ai.StopMoving(); Save(); Say(DisplayName + ": " + reason + (building ? " Unspent items are kept; deposited materials remain in unfinished PlanBuild pieces." : " All unspent ingredients remain in my inventory.")); }
        private Piece.Requirement[] ActiveRequirements(Recipe recipe, Piece.Requirement[] requirements)
        {
            var needed = requirements.Where(r => r.m_resItem && r.GetAmount(1) > 0).ToArray();
            if (recipe && recipe.m_requireOnlyOneIngredient) return needed.OrderByDescending(r => UsableCount(r.m_resItem.name) >= r.GetAmount(1)).ThenBy(r => r.GetAmount(1)).Take(1).ToArray();
            return needed;
        }
        private void TickCraftPlan(float dt)
        {
            if (craftPlan == null) { mode = "stay"; return; }
            if (craftPlan.Construction != null) {
                try { if (!PrepareConstruction(dt)) return; }
                catch (Exception e) { BlockCraft("PlanBuild planning failed: " + (e.InnerException?.Message ?? e.Message)); return; }
            }
            if (Time.time - craftPlan.Started > (craftPlan.Construction != null ? 3600 : 600)) { BlockCraft("This crafting trip reached its time limit. Ask to finish the plan to continue construction."); return; }
            if (Time.time < craftAt) { ai.StopMoving(); return; }
            var current = craftPlan.Dependencies.Count > 0 ? craftPlan.Dependencies.Peek() : craftPlan.Recipe;
            var requirements = ActiveRequirements(current, current ? current.m_resources : craftPlan.Requirements);
            foreach (var requirement in requirements) {
                string prefab = requirement.m_resItem.name;
                int missing = requirement.GetAmount(1) - UsableCount(prefab); if (missing <= 0) continue;
                craftNote = "Need " + missing + " " + Localization.instance.Localize(requirement.m_resItem.m_itemData.m_shared.m_name);
                if (UseStoredMaterials && HasBase && Vector3.Distance(Player.transform.position, basePoint) < 45) foreach (var chest in Chests()) {
                    var stored = chest.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab && i.m_dropPrefab.name == prefab);
                    if (stored == null) continue;
                    int amount = Math.Min(missing, stored.m_stack);
                    if (!Body.GetInventory().CanAddItem(stored, amount)) { BlockCraft("I need more inventory space for " + prefab + "."); return; }
                    if (!WalkBase(dt, chest)) return;
                    Transfer(chest.GetInventory(), Body.GetInventory(), stored, amount, chest); craftAt = Time.time + .5f; return;
                }
                var dependency = KnownRecipe(prefab);
                if (dependency) {
                    if (craftPlan.Dependencies.Count >= 6 || dependency == craftPlan.Recipe || craftPlan.Dependencies.Contains(dependency)) { BlockCraft("That recipe has a circular or overly deep ingredient chain."); return; }
                    craftPlan.Dependencies.Push(dependency); return;
                }
                if (Body.GetInventory().GetEmptySlots() == 0) { BlockCraft("My inventory is full. Ask me to return or store cargo."); return; }
                resource = prefab; itemFilter = ""; goal = Math.Min(100, missing); gathered = 0; anchor = Player.transform.position; orderTime = Time.time; scanAt = 0; target = null; excluded.Clear();
                mode = "gather"; Save(); return;
            }
            if (craftPlan.Action == "gather_recipe" && craftPlan.Dependencies.Count == 0) {
                string name = craftPlan.Name; SetQuestStatus("Complete", "Materials collected. Bringing them back."); craftPlan = null; mode = "return"; Save(); Say("Materials for " + name + " collected. Bringing them back."); return;
            }
            if (current) {
                var stationType = current.GetRequiredStation(1);
                if (stationType) {
                    var station = CraftingStation.FindClosestStationInRange(stationType.m_name, Player.transform.position, 40);
                    if (!station || station.GetLevel() < current.GetRequiredStationLevel(1)) { BlockCraft("Need a nearby " + Localization.instance.Localize(stationType.m_name) + " at level " + current.GetRequiredStationLevel(1) + "."); return; }
                    if (!WalkBase(dt, station)) return;
                    if (!station.CheckUsable(Player, false)) { BlockCraft("The crafting station needs shelter or a fire before it can be used."); return; }
                    station.PokeInUse();
                }
                var output = current.m_item.m_itemData.Clone(); output.m_dropPrefab = current.m_item.gameObject; output.m_stack = current.m_amount; output.m_quality = 1;
                output.m_crafterID = Owner; output.m_crafterName = DisplayName; output.m_customData.Remove("rune.loan"); output.m_customData.Remove("rune.starter");
                if (craftPlan.Dependencies.Count == 0) output.m_customData[IsPersonalEquipment(output) ? "rune.personal" : "rune.kept"] = "1";
                var consumed = ConsumeIngredients(requirements);
                if (consumed == null) { BlockCraft("The ingredients changed before crafting."); return; }
                if (!Body.GetInventory().CanAddItem(output, output.m_stack)) { RestoreIngredients(consumed); BlockCraft("Make room in my inventory for the crafted item. I kept the ingredients."); return; }
                var stockBefore = Body.GetInventory().GetAllItems().ToDictionary(i => i, i => i.m_stack);
                if (!Body.GetInventory().AddItem(output)) { RestoreIngredients(consumed); BlockCraft("Could not store the crafted item."); return; }
                // Native AddItem merges stackable outputs without copying custom metadata.
                if (craftPlan.Dependencies.Count == 0 && !IsPersonalEquipment(output)) foreach (var stack in Body.GetInventory().GetAllItems().Where(i => i.m_shared.m_name == output.m_shared.m_name && (!stockBefore.TryGetValue(i, out int count) || i.m_stack > count))) stack.m_customData["rune.kept"] = "1";
                Save(); craftAt = Time.time + 2;
                if (craftPlan.Dependencies.Count > 0) { craftPlan.Dependencies.Pop(); return; }
                string name = craftPlan.Name; string kept = KeepCraftedItem(output); SetQuestStatus("Complete", "Crafted. " + kept); craftPlan = null; mode = "follow"; Save(); Say("Crafted " + name + " from real ingredients. " + kept); return;
            }
            if (craftPlan.Construction != null) {
                try { BuildConstruction(dt); }
                catch (Exception e) { BlockCraft("PlanBuild construction failed: " + (e.InnerException?.Message ?? e.Message)); }
            } else if (craftPlan.Workbench) BuildWorkbench(dt, requirements); else BuildBoat(requirements);
        }
        private List<ItemDrop.ItemData> ConsumeIngredients(Piece.Requirement[] requirements)
        {
            if (requirements.Any(r => UsableCount(r.m_resItem.name) < r.GetAmount(1))) return null;
            var consumed = new List<ItemDrop.ItemData>();
            foreach (var requirement in requirements) {
                int remaining = requirement.GetAmount(1);
                foreach (var item in Body.GetInventory().GetAllItems().Where(i => IsStorable(i) && i.m_dropPrefab && i.m_dropPrefab.name == requirement.m_resItem.name).ToArray()) {
                    int take = Math.Min(item.m_stack, remaining); var copy = item.Clone(); copy.m_stack = take;
                    if (!Body.GetInventory().RemoveItem(item, take)) { RestoreIngredients(consumed); return null; }
                    consumed.Add(copy); remaining -= take; if (remaining == 0) break;
                }
            }
            return consumed;
        }
        private void RestoreIngredients(List<ItemDrop.ItemData> items) { foreach (var item in items) if (!Body.GetInventory().AddItem(item)) ItemDrop.DropItem(item, item.m_stack, transform.position + Vector3.up, Quaternion.identity); Save(); }
        private void BuildBoat(Piece.Requirement[] requirements)
        {
            var piece = craftPlan.Boat.GetComponent<Piece>();
            var forward = Player.transform.forward; forward.y = 0; forward.Normalize();
            var location = Player.transform.position + forward * 8; location.y = WaterAt(location) + .3f;
            if (Vector3.Distance(transform.position, Player.transform.position) > 4) { BlockCraft("Come within four metres of me and face open water before building the boat."); return; }
            if (piece.m_craftingStation && !CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, location)) { BlockCraft("The launch spot needs a workbench in range. Stand at shore and face open water."); return; }
            // Validate a conservative hull footprint against the seabed and existing structures.
            float length = craftPlan.Boat.name == "VikingShip" ? 12 : craftPlan.Boat.name == "Karve" ? 8 : 5;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++) {
                var point = location + Player.transform.right * x * 2.5f + forward * z * length * .5f;
                if (!Heightmap.GetHeight(point, out float ground) || ground > WaterAt(point) - 1.5f) { BlockCraft("The water ahead is too shallow or the seabed is not loaded. Face a clear, deeper launch spot."); return; }
            }
            var overlaps = Physics.OverlapBox(location + Vector3.up, new Vector3(2.5f, 2, length * .5f), Quaternion.LookRotation(forward), LayerMask.GetMask("piece", "static_solid", "Default"));
            if (overlaps.Any(c => c && !c.isTrigger && (c.GetComponentInParent<Piece>() || c.GetComponentInParent<Ship>() || c.GetComponentInParent<Character>()))) { BlockCraft("There is a structure, boat or creature in the launch space."); return; }
            var ingredients = ConsumeIngredients(requirements); if (ingredients == null) { BlockCraft("Boat ingredients are missing."); return; }
            GameObject boat = null;
            try {
                boat = UnityEngine.Object.Instantiate(craftPlan.Boat, location, Quaternion.LookRotation(forward));
                if (!boat.GetComponent<ZNetView>().IsValid()) throw new InvalidOperationException("Boat network object unavailable.");
                boat.GetComponent<Piece>().AssignCreator(Owner);
                string name = craftPlan.Name; SetQuestStatus("Complete", "Boat built."); craftPlan = null; mode = "stay"; anchor = transform.position; Save(); Say("Built " + name + " in the water ahead. All recipe ingredients were consumed.");
            } catch (Exception e) { if (boat) ZNetScene.instance.Destroy(boat); RestoreIngredients(ingredients); BlockCraft("Boat placement failed: " + e.Message); }
        }
    }
}


